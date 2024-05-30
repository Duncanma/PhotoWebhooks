using PhotoWebhooks;
using System;

public static class FunctionsHelpers
{

    private static string[] crawlerStrings = new string[] {
            "Bytespider", "AhrefsBot", "bingbot", "Baiduspider", "Googlebot" };

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
        bool isCrawler = false;
        for (int i = 0; i < crawlerStrings.Length; i++)
        {
            if (request.user_agent.Contains(crawlerStrings[i]))
            {
                isCrawler = true;
                break;
            }
        }

        return isCrawler;
    }

    public static bool RequestIsLocal(RequestRecord request)
    {
        string url = request.url ?? string.Empty;

        return url.StartsWith("http://localhost");

    }
}