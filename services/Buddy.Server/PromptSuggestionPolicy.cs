using System.Text.RegularExpressions;
namespace Buddy.Server;

public static class PromptSuggestionPolicy
{
    public static bool PrivateMetadata(string title,string field)=>Regex.IsMatch(title+" "+field,@"\b(incognito|inprivate|private|bank|banking|payment|password|passcode|secret|credential|medical|health|financial)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static bool ShouldOffer(string text)=>text.Length is >=24 and <=4000 && Security.Redact(text)==text && !PrivateMetadata("",text) &&
        Regex.IsMatch(text,@"\b(write|help|create|explain|summarize|analyse|analyze|draft|teach|make|review|compare|plan|please|how|what|why)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static bool? Reply(string text)=>text.Trim().TrimEnd('.','!').ToLowerInvariant() switch {"yes" or "yes please" or "refine it" or "yes refine it"=>true,"no" or "no thanks" or "dismiss"=>false,_=>null};
    public static (string Prefix,string Removed,string Added,string Suffix) Difference(string before,string after){
        int start=0;while(start<Math.Min(before.Length,after.Length)&&before[start]==after[start])start++;
        int end=0;while(end<Math.Min(before.Length,after.Length)-start&&before[^(end+1)]==after[^(end+1)])end++;
        return(before[..start],before.Substring(start,before.Length-start-end),after.Substring(start,after.Length-start-end),end==0?"":before[^end..]);
    }
}
