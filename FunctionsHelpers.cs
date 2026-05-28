using PhotoWebhooks;
using System;

public static class FunctionsHelpers
{

    private static readonly string[] crawlerStrings = new string[] {
            "Bytespider", "AhrefsBot", "bingbot", "Baiduspider", "Googlebot",
            "ClaudeBot", "TikTokSpider", "python-requests", "facebookexternalhit",
            "SemrushBot", "DotBot", "YandexBot", "MJ12bot", "PetalBot" };

    public static string SimplifyReferrer(string referrer)
    {
        Uri uri;

        if (Uri.TryCreate(referrer, UriKind.Absolute, out uri))
        {
            string host = uri.Host.ToLower();

            if (host.Contains("reddit."))
            {
                return "Reddit";
            }

            if (host.Contains("linkedin."))
            {
                return "LinkedIn";
            }

            if (host.Contains("google."))
            {
                return "Google";
            }
            if (host.Contains("bing."))
            {
                return "Bing";
            }

            if (host.Contains("instagram.com"))
            {
                return "Instagram";
            }

            if (host.Contains("facebook.com"))
            {
                return "Facebook";
            }

            if (host.Contains("yandex.ru"))
            {
                return "Yandex";
            }

            if (host.Contains("dev.to"))
            {
                return "Dev.To";
            }


            return host;
        }

        if (referrer == "android-app://com.slack/")
        {
            return "Slack";
        }

        return null;

    }

    public static bool RequestIsCrawler(RequestRecord request)
    {
        return RequestIsCrawler(request, out _);
    }

    public static bool RequestIsCrawler(RequestRecord request, out string matchedSignature)
    {
        string userAgent = request?.user_agent ?? string.Empty;
        matchedSignature = null;
        for (int i = 0; i < crawlerStrings.Length; i++)
        {
            if (userAgent.IndexOf(crawlerStrings[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matchedSignature = crawlerStrings[i];
                return true;
            }
        }

        return false;
    }

    public static bool RequestIsLocal(RequestRecord request)
    {
        string url = request.url ?? string.Empty;

        return url.StartsWith("http://localhost");

    }
}