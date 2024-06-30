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
    }
}