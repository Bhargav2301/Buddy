using Buddy.Server;

namespace Buddy.Windows;

// Only deliberately asked questions and validated answers; no screenshots or target coordinates.
internal sealed class TeachingConversation
{
    private readonly List<TeachingExchange> exchanges=[];
    private IntPtr window;
    private string app="",title="";
    private DateTimeOffset last;
    internal IntPtr Window=>window;
    internal bool IsCurrent(string currentApp,string currentTitle,DateTimeOffset now)=>app==currentApp&&title==currentTitle&&now-last<=TimeSpan.FromMinutes(10);
    internal IReadOnlyList<TeachingExchange> For(IntPtr selected,string selectedApp,string selectedTitle,DateTimeOffset now)
    {
        if(window!=selected||app!=selectedApp||title!=selectedTitle||now-last>TimeSpan.FromMinutes(10))Clear();
        window=selected;app=selectedApp;title=selectedTitle;last=now;
        return exchanges.ToArray();
    }
    internal void Add(string question,string answer,DateTimeOffset now)
    {
        if(window==IntPtr.Zero)return;
        exchanges.Add(new(Security.Redact(question),Security.Redact(answer)));last=now;
        while(exchanges.Count>10||exchanges.Sum(e=>e.Question.Length+e.Answer.Length)>12000)exchanges.RemoveAt(0);
    }
    internal void Clear(){exchanges.Clear();window=IntPtr.Zero;app=title="";last=default;}
}
