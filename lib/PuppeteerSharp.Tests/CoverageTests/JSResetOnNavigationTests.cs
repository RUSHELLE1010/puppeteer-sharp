using System.Threading.Tasks;
using NUnit.Framework;
using PuppeteerSharp.Nunit;
using PuppeteerSharp.PageCoverage;
using PuppeteerSharp.Tests.Attributes;

namespace PuppeteerSharp.Tests.CoverageTests
{
    public class JSResetOnNavigationTests : PuppeteerPageBaseTest
    {
        public JSResetOnNavigationTests() : base()
        {
        }

        [PuppeteerTest("coverage.spec.ts", "resetOnNavigation", "should report scripts across navigations when disabled")]
        [Skip(SkipAttribute.Targets.Firefox)]
        public async Task ShouldReportScriptsAcrossNavigationsWhenDisabled()
        {
            await Page.Coverage.StartJSCoverageAsync(new CoverageStartOptions
            {
                ResetOnNavigation = false
            });
            await Page.GoToAsync(TestConstants.ServerUrl + "/jscoverage/multiple.html");
            await Page.GoToAsync(TestConstants.EmptyPage);
            var coverage = await Page.Coverage.StopJSCoverageAsync();
            Assert.AreEqual(2, coverage.Length);
        }

        [PuppeteerTest("coverage.spec.ts", "resetOnNavigation", "should NOT report scripts across navigations when enabled")]
        [Skip(SkipAttribute.Targets.Firefox)]
        public async Task ShouldNotReportScriptsAcrossNavigationsWhenEnabled()
        {
            await Page.Coverage.StartJSCoverageAsync();
            await Page.GoToAsync(TestConstants.ServerUrl + "/jscoverage/multiple.html");
            await Page.GoToAsync(TestConstants.EmptyPage);
            var coverage = await Page.Coverage.StopJSCoverageAsync();
            Assert.IsEmpty(coverage);
        }
    }
}
