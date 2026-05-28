using Microsoft.VisualStudio.TestTools.UnitTesting;
using PhotoWebhooks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoWebhooks.Tests
{
    [TestClass()]
    public class FunctionsTests
    {
        [TestMethod()]
        public void SimplifyReferrerTest()
        {

            Assert.AreEqual<string>(FunctionsHelpers.SimplifyReferrer("https://fred.com"), "fred.com");
            Assert.AreEqual<string>(FunctionsHelpers.SimplifyReferrer("https://google.com"), "Google");
            Assert.AreEqual<string>(FunctionsHelpers.SimplifyReferrer("https://dev.to/thispage"), "Dev.To");
            Assert.AreEqual<string>(FunctionsHelpers.SimplifyReferrer("https://google.ca"), "Google");
        }

        [TestMethod()]
        public void RequestIsCrawlerTest()
        {
            Assert.IsTrue(FunctionsHelpers.RequestIsCrawler(
                new RequestRecord { user_agent = "Mozilla/5.0 (compatible; ClaudeBot/1.0; +https://www.anthropic.com/bot)" }));
            Assert.IsTrue(FunctionsHelpers.RequestIsCrawler(
                new RequestRecord { user_agent = "python-requests/2.31.0" }));
            Assert.IsFalse(FunctionsHelpers.RequestIsCrawler(
                new RequestRecord { user_agent = "Mozilla/5.0 (Macintosh; Intel Mac OS X) AppleWebKit/537.36 Chrome/124.0" }));
            Assert.IsFalse(FunctionsHelpers.RequestIsCrawler(
                new RequestRecord { user_agent = null }));
        }

        [TestMethod()]
        public void RequestIsCrawlerReturnsMatchedSignatureTest()
        {
            bool result = FunctionsHelpers.RequestIsCrawler(
                new RequestRecord { user_agent = "python-requests/2.31.0" },
                out string matchedSignature);

            Assert.IsTrue(result);
            Assert.AreEqual("python-requests", matchedSignature);
        }
    }
}