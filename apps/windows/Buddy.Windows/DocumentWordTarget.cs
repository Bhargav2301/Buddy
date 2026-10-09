using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

internal sealed class DocumentWordTarget(AutomationElement editor,TextPatternRange range)
{
    internal bool IsCurrent(ScreenElement element){
        try {var bounds=range.GetBoundingRectangles();return editor.Current.IsEnabled&&!editor.Current.IsOffscreen&&!editor.Current.IsPassword&&range.GetText(81).Equals(element.Name,StringComparison.OrdinalIgnoreCase)&&bounds.Length==1&&bounds[0]==new Rect(element.X,element.Y,element.Width,element.Height);}
        catch{return false;}
    }
    internal static void Collect(AutomationElement editor,ScreenElement parent,string requested,List<ScreenElement> elements,Dictionary<string,AutomationElement> nodes,Dictionary<string,DocumentWordTarget> words,CancellationToken ct){
        if(requested.Length is <1 or >80||parent.Role is not("Edit" or "Document")||!editor.TryGetCurrentPattern(TextPattern.Pattern,out var pattern))return;
        var doc=((TextPattern)pattern).DocumentRange;var content=doc.GetText(8192);
        if(Security.Redact(content)!=content)return;
        foreach(Match match in Regex.Matches(content,@"(?<![\p{L}\p{N}_])"+Regex.Escape(requested)+@"(?![\p{L}\p{N}_])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Cast<Match>().Take(3)){
            ct.ThrowIfCancellationRequested();var span=doc.Clone();span.MoveEndpointByRange(TextPatternRangeEndpoint.End,span,TextPatternRangeEndpoint.Start);
            span.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,match.Index+match.Length);span.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,match.Index);
            if(!span.GetText(81).Equals(requested,StringComparison.OrdinalIgnoreCase))continue;
            var bounds=span.GetBoundingRectangles();if(bounds.Length!=1||bounds[0].IsEmpty||!double.IsFinite(bounds[0].X)||!double.IsFinite(bounds[0].Y)||bounds[0].Width<=0||bounds[0].Height<=0)continue;
            var rectangle=bounds[0];
            if(!new Rect(parent.X,parent.Y,parent.Width,parent.Height).Contains(rectangle))continue;
            var id="word:"+parent.Ref+":"+match.Index;
            var element=new ScreenElement(id,span.GetText(81),"Text",rectangle.X,rectangle.Y,rectangle.Width,rectangle.Height);
            elements.Add(element);nodes[id]=editor;words[id]=new(editor,span);
        }
    }
}
