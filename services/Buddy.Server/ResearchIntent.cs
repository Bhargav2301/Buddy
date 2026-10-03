using System.Text.RegularExpressions;
namespace Buddy.Server;

public static class ResearchIntent
{
    public static bool UseWeb(string text,bool enabled)
    {
        if(!enabled)return false;
        const RegexOptions options=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
        var limit=TimeSpan.FromMilliseconds(100);
        if(Regex.IsMatch(text,@"https?://|\b(search|research|sources|citations|look up|latest|current|today|news|weather|verify)\b",options,limit))return true;
        return !Regex.IsMatch(text,@"\b(write|compose|draft|rewrite|refine|improve|create)\b[\s\S]{0,300}\b(poem|poetry|story|fiction|email|invitation|prompt|sentence|paragraph|song)\b",options,limit);
    }
}
