using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;

namespace Buddy.Windows;

// Native optional job UI. It owns no input-dispatch or connector capability.
internal sealed class TaskCenter : IDisposable
{
    private readonly BuddyService service;
    private readonly Action stopActions;
    private readonly Action<string> plan;
    private Window? window;
    private readonly StackPanel entries=new();
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox app=new(){Text="notepad",MaxLength=60};
    private readonly TextBox version=new(){Text="unspecified",MaxLength=100};
    private readonly TextBox goal=new(){MaxLength=4000,MinHeight=55,TextWrapping=TextWrapping.Wrap};
    private CancellationTokenSource? operation;
    private string? localJob;
    private Task persistence=Task.CompletedTask;
    internal JobLedger Ledger { get; }=new();
    internal TaskCenter(BuddyService service,Action stopActions,Action<string> plan)
    {
        this.service=service;this.stopActions=stopActions;this.plan=plan;
        Ledger.Changed+=Changed;
    }
    internal async Task Initialize()
    {
        Ledger.Restore(await service.Store.Read(s=>s.Jobs));Changed();
    }
    private void Changed()
    {
        var snapshot=Ledger.Snapshot.ToList();
        persistence=persistence.ContinueWith(async _=>{try{await service.Store.Update(s=>{s.Jobs=snapshot;return true;});}
            catch{System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(()=>notice.Text="Job history could not be saved; current results remain visible."));}}).Unwrap();
        Refresh();
    }
    internal void Open(string selectedApp="")
    {
        if(selectedApp.Length>0)app.Text=selectedApp;
        if(window is null) {
            window=new(){Title="Buddy - Local jobs and knowledge",Width=680,Height=700,Background=BuddyTheme.Surface,Foreground=BuddyTheme.Ink,FontFamily=BuddyTheme.Font};
            var body=new StackPanel{Margin=new(20)};
            void Label(string text)=>body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,5)});
            void Add(string text,Action action){var b=new Button{Content=text,Margin=new(0,4,0,4),Padding=new(8)};b.Click+=(_,_)=>action();body.Children.Add(b);}
            Label("Optional local jobs - one at a time");
            Label("Action jobs show a full plan and pause for approvals. Note searches return excerpts from explicitly imported files; they never run instructions found there. Interrupted jobs require review after restart.");
            Label("Task or note search");body.Children.Add(goal);AutomationProperties.SetName(goal,"Local job goal");
            Add("Review an action plan",()=>plan(goal.Text));
            Label("Exact app process name (for example notepad or FL64)");body.Children.Add(app);AutomationProperties.SetName(app,"Knowledge app process");
            Label("App version covered by your notes");body.Children.Add(version);AutomationProperties.SetName(version,"Knowledge app version");
            Add("Search imported app notes",()=>_ = Search(app.Text,goal.Text));
            Add("Import a local notes folder",()=>{
                var picker=new Microsoft.Win32.OpenFolderDialog{Title="Choose app notes: visible .md/.txt files, no subfolders"};
                if(picker.ShowDialog(window)==true)_=Import(picker.FolderName,app.Text,version.Text);
            });
            Add("Inspect / remove imported sources",()=>_ = ShowSources());
            Add("Stop current job",()=>{Cancel();stopActions();});
            Add("Clear job history",()=>{try{Ledger.ClearHistory();}catch(Exception e){notice.Text=e.Message;}});
            body.Children.Add(notice);body.Children.Add(entries);
            window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            window.SourceInitialized+=(_,_)=>CaptureProtection.Apply(new WindowInteropHelper(window).Handle);
            window.Closing+=(_,e)=>{e.Cancel=true;window.Hide();};
            window.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Cancel();stopActions();}};
        }
        Refresh();window.Show();window.Activate();
    }
    internal async Task Search(string selectedApp,string query)
    {
        if(operation is not null){notice.Text="Stop the current local job first.";return;}
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(15));operation=cts;string? id=null;
        try {
            selectedApp=LocalKnowledge.AppKey(selectedApp);query=Security.Text(query,1000,"Search");
            id=Ledger.Begin("notes",query,selectedApp);localJob=id;Open(selectedApp);
            Ledger.Move(id,JobState.Running,"Searching encrypted imported notes on this PC");
            var packs=await service.Store.Read(s=>s.Knowledge);cts.Token.ThrowIfCancellationRequested();
            var hits=await Task.Run(()=>LocalKnowledge.Search(packs,selectedApp,query,cts.Token),cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var result=hits.Count==0?"No matching imported lines. Import notes for this exact app or change the query.":string.Join("\n\n",hits.Select(h=>$"{h.RelativePath}:{h.Line} | app version {h.AppVersion} | revision {h.Revision[..12]}\n{h.Text}"));
            Ledger.Receipt(id,result,true);Ledger.Move(id,JobState.Verifying,"Search finished; these are sourced excerpts, not verified application behavior");
            Ledger.Move(id,JobState.Completed,$"Search complete: {hits.Count} matching excerpts. Imported notes may be outdated.");
        } catch(OperationCanceledException){if(id is not null)Ledger.Move(id,JobState.Cancelled,"Search stopped; no further work runs.");}
        catch(Exception e){notice.Text=e.Message;if(id is not null)Ledger.Move(id,JobState.Failed,e.Message);}
        finally{if(ReferenceEquals(operation,cts)){operation=null;localJob=null;}}
    }
    internal async Task Import(string folder,string selectedApp,string appVersion)
    {
        if(operation is not null){notice.Text="Stop the current local job first.";return;}
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(20));operation=cts;string? id=null;
        try {
            id=Ledger.Begin("import","Import selected notes folder",LocalKnowledge.AppKey(selectedApp));localJob=id;
            Ledger.Move(id,JobState.Running,"Reading only the selected folder's visible text and Markdown files");
            var pack=await LocalKnowledge.Import(folder,selectedApp,appVersion,cts.Token,text=>Ledger.Progress(id,text));
            cts.Token.ThrowIfCancellationRequested();
            // Once the atomic local commit begins, cancellation reports review rather than pretending it was undone.
            await service.Store.Update(s=>{cts.Token.ThrowIfCancellationRequested();LocalKnowledge.StorePack(s,pack);return true;});
            if(cts.IsCancellationRequested){notice.Text="Import completed its local commit while Stop arrived. Inspect/remove the saved source if unwanted.";return;}
            Ledger.Receipt(id,$"Imported {pack.Documents.Count} files from {pack.SourceFolder}\nApp {pack.App}, version {pack.AppVersion}, revision {pack.Revision}",true);
            Ledger.Move(id,JobState.Verifying,"Verified bounded source files and content hashes");Ledger.Move(id,JobState.Completed,"Versioned notes saved encrypted on this PC. No background folder watch.");
        }catch(OperationCanceledException){if(id is not null)Ledger.Move(id,JobState.Cancelled,"Import stopped before commit; inspect sources if Stop arrived during commit.");}
        catch(Exception e){notice.Text=e.Message;if(id is not null)Ledger.Move(id,JobState.Failed,e.Message);}
        finally{if(ReferenceEquals(operation,cts)){operation=null;localJob=null;}}
    }
    private async Task ShowSources()
    {
        try{
            var packs=await service.Store.Read(s=>s.Knowledge);var panel=new StackPanel{Margin=new(20)};
            foreach(var pack in packs){
                panel.Children.Add(new TextBlock{Text=$"{pack.App} | version {pack.AppVersion}\n{pack.SourceFolder}\nImported {pack.Imported:u}\nRevision {pack.Revision}\n"+string.Join("\n",pack.Documents.Select(d=>d.RelativePath+" | "+d.Sha256)),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
                var remove=new Button{Content="Remove this imported copy"};remove.Click+=async(_,_)=>{try{await service.Store.Update(s=>s.Knowledge.RemoveAll(p=>p.Id==pack.Id));remove.IsEnabled=false;remove.Content="Removed; original files unchanged";}catch(Exception e){notice.Text=e.Message;}};panel.Children.Add(remove);
            }
            if(packs.Count==0)panel.Children.Add(new TextBlock{Text="No imported sources."});
            var viewer=new Window{Title="Buddy - Knowledge sources",Width=640,Height=500,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=BuddyTheme.Surface,Foreground=BuddyTheme.Ink};
            viewer.SourceInitialized+=(_,_)=>CaptureProtection.Apply(new WindowInteropHelper(viewer).Handle);viewer.Show();
        }catch(Exception e){notice.Text=e.Message;}
    }
    private void Refresh()
    {
        entries.Children.Clear();
        foreach(var job in Ledger.Snapshot.Reverse())entries.Children.Add(new TextBlock{Text=$"{job.Kind.ToUpperInvariant()} | {job.State} | {job.Updated:t}\n{job.Goal}\n{job.Status}\n"+string.Join("\n",job.Receipts.Select(r=>$"{r.Sequence}. {(r.Success?"Observed":"Not completed")}: {r.Detail}")),TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,12)});
    }
    internal void Cancel(){operation?.Cancel();if(localJob is not null)Ledger.Move(localJob,JobState.Cancelled,"Stop requested. No further work; inspect any result already committed.");}
    internal Task Flush()=>persistence;
    public void Dispose(){Cancel();window?.Hide();Ledger.Changed-=Changed;}
}
