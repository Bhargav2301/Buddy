using System.Text.RegularExpressions;
namespace Buddy.Server;
public static class WorkflowIntent
{
    public static bool NeedsSpecialists(string query)=>Regex.IsMatch(query,@"\b(teach|explain|learn|guide)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))&&Regex.IsMatch(query,@"\b(and|then|also)\b.{0,200}\b(write|type|insert|open|create|do|perform|set up)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
}
