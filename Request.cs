using Microsoft.WindowsAzure.Storage.Table;
using System;


namespace PhotoWebhooks
{
    public class RequestRecord
    {
        public string id {  get; set; }
        public string requestTime { get; set; }

        //simplified version of the day portion (20240503)
        public string day { get; set; }
        //the permalink/canonical relative form of my URLs (like /about or /blog)
        public string page { get; set; }

        //the full URL that was requested, with whatever query strings might be on it
        public string url { get; set; }

        public string ip_address { get; set; }

        public string title { get; set; }
        public string referrer { get; set; }
        public string simple_referrer { get; set; }
        public string accept_lang { get; set; }
        public string user_agent { get; set; }
        public string country { get; set; }
        public string countryName { get; set; }
        public bool js_enabled { get; set; }
        public string platform { get; set; }
        public string device { get; set; }
        public string browser { get; set; }
        public bool isSpider { get; set; }
        public string visit { get; set; }
        public bool startVisit {  get; set; }
        public string visitor { get; set; }
        public bool newVisitor { get; set; }
    }

    public class ViewsByDate
    {
        public string dateType { get; set; }
        public string id { get; set; } //date
        public string views { get; set; }
    }

    public class ViewsByPathByDate
    {
        public string dateType { get; set; }
        public string id { get; set; } //date
        public string page { get; set; }
        public string views { get; set; }
    }



}
