// * MIT License
//  *
//  * Copyright (c) Darío Kondratiuk
//  *
//  * Permission is hereby granted, free of charge, to any person obtaining a copy
//  * of this software and associated documentation files (the "Software"), to deal
//  * in the Software without restriction, including without limitation the rights
//  * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  * copies of the Software, and to permit persons to whom the Software is
//  * furnished to do so, subject to the following conditions:
//  *
//  * The above copyright notice and this permission notice shall be included in all
//  * copies or substantial portions of the Software.
//  *
//  * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  * SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using PuppeteerSharp.Helpers;
using PuppeteerSharp.Nunit;
using PuppeteerSharp.Tests.Attributes;

namespace PuppeteerSharp.Tests.RequestInterceptionExperimentalTests;

public class PageSetRequestInterceptionTests : PuppeteerPageBaseTest
{
    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should cooperatively ${expectedAction} by priority")]
    [Skip(SkipAttribute.Targets.Firefox)]
    [TestCase("abort")]
    [TestCase("continue")]
    [TestCase("respond")]
    public async Task ShouldCooperativelyActByPriority(string expectedAction)
    {
        var actionResults = new List<string>();

        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request =>
        {
            if (request.Url.EndsWith(".css"))
            {
                var headers = request.Headers;
                headers["xaction"] = "continue";
                return request.ContinueAsync(new Payload() { Headers = headers, },
                    expectedAction == "continue" ? 1 : 0);
            }

            return request.ContinueAsync(new Payload(), 0);
        });

        Page.AddRequestInterceptor(request =>
        {
            if (request.Url.EndsWith(".css"))
            {
                Dictionary<string, object> headers = [];
                foreach (var kvp in request.Headers)
                {
                    headers.Add(kvp.Key, kvp.Value);
                }

                headers["xaction"] = "respond";
                return request.RespondAsync(new ResponseData() { Headers = headers, },
                    expectedAction == "respond" ? 1 : 0);
            }

            return request.ContinueAsync(new Payload(), 0);
        });

        Page.AddRequestInterceptor(request =>
        {
            if (request.Url.EndsWith(".css"))
            {
                var headers = request.Headers;
                headers["xaction"] = "abort";
                return request.AbortAsync(RequestAbortErrorCode.Aborted, expectedAction == "abort" ? 1 : 0);
            }

            return request.ContinueAsync(new Payload(), 0);
        });

        Page.Response += (_, e) =>
        {
            e.Response.Headers.TryGetValue("xaction", out var xaction);

            if (e.Response.Url.EndsWith(".css") && !string.IsNullOrEmpty(xaction))
            {
                actionResults.Add(xaction);
            }
        };

        Page.RequestFailed += (_, e) =>
        {
            if (e.Request.Url.EndsWith(".css"))
            {
                actionResults.Add("abort");
            }
        };

        IResponse response;

        if (expectedAction == "continue")
        {
            var serverRequestTask = Server.WaitForRequest("/one-style.css", request => request.Headers["xaction"]);
            response = await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
            await serverRequestTask;
            actionResults.Add(serverRequestTask.Result);
        }
        else
        {
            response = await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
        }

        Assert.AreEqual(1, actionResults.Count);
        Assert.AreEqual(expectedAction, actionResults[0]);
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception", "should intercept")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldIntercept()
    {
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(async request =>
        {
            if (TestUtils.IsFavicon(request))
            {
                await request.ContinueAsync(new Payload(), 0);
                return;
            }

            StringAssert.Contains("empty.html", request.Url);
            Assert.NotNull(request.Headers);
            Assert.NotNull(request.Headers["user-agent"]);
            Assert.NotNull(request.Headers["accept"]);
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.Null(request.PostData);
            Assert.True(request.IsNavigationRequest);
            Assert.AreEqual(ResourceType.Document, request.ResourceType);
            Assert.AreEqual(Page.MainFrame, request.Frame);
            Assert.AreEqual(TestConstants.AboutBlank, request.Frame.Url);
            await request.ContinueAsync(new Payload(), 0);
        });
        var response = await Page.GoToAsync(TestConstants.EmptyPage);
        Assert.True(response.Ok);

        Assert.AreEqual(TestConstants.Port, response.RemoteAddress.Port);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work when POST is redirected with 302")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWhenPostIsRedirectedWith302()
    {
        Server.SetRedirect("/rredirect", "/empty.html");
        await Page.GoToAsync(TestConstants.EmptyPage);
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(async request => await request.ContinueAsync(new Payload(), 0));

        await Page.SetContentAsync(@"
                <form action='/rredirect' method='post'>
                    <input type='hidden' id='foo' name='foo' value='FOOBAR'>
                </form>
            ");
        await Task.WhenAll(
            Page.QuerySelectorAsync("form").EvaluateFunctionAsync("form => form.submit()"),
            Page.WaitForNavigationAsync()
        );
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work when header manipulation headers with redirect")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWhenHeaderManipulationHeadersWithRedirect()
    {
        Server.SetRedirect("/rredirect", "/empty.html");
        await Page.SetRequestInterceptionAsync(true);

        Page.AddRequestInterceptor(async request =>
        {
            var headers = request.Headers.Clone();
            headers["foo"] = "bar";
            await request.ContinueAsync(new Payload { Headers = headers }, 0);
        });

        await Page.GoToAsync(TestConstants.ServerUrl + "/rrredirect");
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should be able to remove headers")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldBeAbleToRemoveHeaders()
    {
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(async request =>
        {
            var headers = request.Headers.Clone();
            headers["foo"] = "bar";
            headers.Remove("origin");
            await request.ContinueAsync(new Payload { Headers = headers }, 0);
        });

        var requestTask = Server.WaitForRequest("/empty.html", request => request.Headers["origin"]);
        await Task.WhenAll(
            requestTask,
            Page.GoToAsync(TestConstants.ServerUrl + "/empty.html")
        );
        Assert.True(string.IsNullOrEmpty(requestTask.Result));
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should contain referer header")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldContainRefererHeader()
    {
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        var requestsReadyTcs = new TaskCompletionSource<bool>();

        Page.AddRequestInterceptor(async request =>
        {
            if (!TestUtils.IsFavicon(request))
            {
                requests.Add(request);

                if (requests.Count > 1)
                {
                    requestsReadyTcs.TrySetResult(true);
                }
            }

            await request.ContinueAsync(new Payload(), 0);
        });

        await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
        await requestsReadyTcs.Task.WithTimeout();
        StringAssert.Contains("/one-style.css", requests[1].Url);
        StringAssert.Contains("/one-style.html", requests[1].Headers["Referer"]);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should properly return navigation response when URL has cookies")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldProperlyReturnNavigationResponseWhenURLHasCookies()
    {
        // Setup cookie.
        await Page.GoToAsync(TestConstants.EmptyPage);
        await Page.SetCookieAsync(new CookieParam { Name = "foo", Value = "bar" });

        // Setup request interception.
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));
        var response = await Page.ReloadAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should stop intercepting")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldStopIntercepting()
    {
        await Page.SetRequestInterceptionAsync(true);

        async Task EventHandler(IRequest request)
        {
            await request.ContinueAsync(new Payload(), 0);
            Page.RemoveRequestInterceptor(EventHandler);
        }

        Page.AddRequestInterceptor(EventHandler);
        await Page.GoToAsync(TestConstants.EmptyPage);
        await Page.SetRequestInterceptionAsync(false);
        await Page.GoToAsync(TestConstants.EmptyPage);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should show custom HTTP headers")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldShowCustomHTTPHeaders()
    {
        await Page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { ["foo"] = "bar" });
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request =>
        {
            Assert.AreEqual("bar", request.Headers["foo"]);
            return request.ContinueAsync(new Payload(), 0);
        });
        var response = await Page.GoToAsync(TestConstants.EmptyPage);
        Assert.True(response.Ok);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with redirect inside sync XHR")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithRedirectInsideSyncXHR()
    {
        await Page.GoToAsync(TestConstants.EmptyPage);
        Server.SetRedirect("/logo.png", "/pptr.png");
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));

        var status = await Page.EvaluateFunctionAsync<int>(@"async () =>
            {
                const request = new XMLHttpRequest();
                request.open('GET', '/logo.png', false);  // `false` makes the request synchronous
                request.send(null);
                return request.status;
            }");
        Assert.AreEqual(200, status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with custom referer headers")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithCustomRefererHeaders()
    {
        await Page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { ["referer"] = TestConstants.EmptyPage });
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request =>
        {
            Assert.AreEqual(TestConstants.EmptyPage, request.Headers["referer"]);
            return request.ContinueAsync(new Payload(), 0);
        });
        var response = await Page.GoToAsync(TestConstants.EmptyPage);
        Assert.True(response.Ok);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception", "should be abortable")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldBeAbortable()
    {
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request =>
        {
            if (request.Url.EndsWith(".css"))
            {
                return request.AbortAsync(RequestAbortErrorCode.Failed, 0);
            }
            else
            {
                return request.ContinueAsync(new Payload(), 0);
            }
        });
        var failedRequests = 0;
        Page.RequestFailed += (_, _) => failedRequests++;
        var response = await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
        Assert.True(response.Ok);
        Assert.Null(response.Request.Failure);
        Assert.AreEqual(1, failedRequests);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should be abortable with custom error codes")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldBeAbortableWithCustomErrorCodes()
    {
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.AbortAsync(RequestAbortErrorCode.InternetDisconnected, 0));
        IRequest failedRequest = null;
        Page.RequestFailed += (_, e) => failedRequest = e.Request;

        var exception = Assert.ThrowsAsync<NavigationException>(
            () => Page.GoToAsync(TestConstants.EmptyPage));

        StringAssert.StartsWith("net::ERR_INTERNET_DISCONNECTED", exception.Message);
        Assert.NotNull(failedRequest);
        Assert.AreEqual("net::ERR_INTERNET_DISCONNECTED", failedRequest.Failure);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception", "should send referer")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldSendReferer()
    {
        await Page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { ["referer"] = "http://google.com/" });
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));
        var requestTask = Server.WaitForRequest("/grid.html", request => request.Headers["referer"].ToString());
        await Task.WhenAll(
            requestTask,
            Page.GoToAsync(TestConstants.ServerUrl + "/grid.html")
        );
        Assert.AreEqual("http://google.com/", requestTask.Result);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should fail navigation when aborting main resource")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldFailNavigationWhenAbortingMainResource()
    {
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.AbortAsync(RequestAbortErrorCode.Failed, 0));
        var exception = Assert.ThrowsAsync<NavigationException>(
            () => Page.GoToAsync(TestConstants.EmptyPage));

        if (TestConstants.IsChrome)
        {
            StringAssert.Contains("net::ERR_FAILED", exception.Message);
        }
        else
        {
            StringAssert.Contains("NS_ERROR_FAILURE", exception.Message);
        }
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with redirects")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithRedirects()
    {
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(async request =>
        {
            await request.ContinueAsync(new Payload(), 0);
            requests.Add(request);
        });

        Server.SetRedirect("/non-existing-page.html", "/non-existing-page-2.html");
        Server.SetRedirect("/non-existing-page-2.html", "/non-existing-page-3.html");
        Server.SetRedirect("/non-existing-page-3.html", "/non-existing-page-4.html");
        Server.SetRedirect("/non-existing-page-4.html", "/empty.html");
        var response = await Page.GoToAsync(TestConstants.ServerUrl + "/non-existing-page.html");
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
        StringAssert.Contains("empty.html", response.Url);
        Assert.AreEqual(5, requests.Count);
        Assert.AreEqual(ResourceType.Document, requests[2].ResourceType);

        // Check redirect chain
        var redirectChain = response.Request.RedirectChain;
        Assert.AreEqual(4, redirectChain.Length);
        StringAssert.Contains("/non-existing-page.html", redirectChain[0].Url);
        StringAssert.Contains("/non-existing-page-3.html", redirectChain[2].Url);

        for (var i = 0; i < redirectChain.Length; ++i)
        {
            var request = redirectChain[i];
            Assert.True(request.IsNavigationRequest);
            Assert.AreEqual(request, request.RedirectChain.ElementAt(i));
        }
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with redirects for subresources")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithRedirectsForSubresources()
    {
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(request =>
        {
            if (!TestUtils.IsFavicon(request))
            {
                requests.Add(request);
            }

            return request.ContinueAsync(new Payload(), 0);
        });

        Server.SetRedirect("/one-style.css", "/two-style.css");
        Server.SetRedirect("/two-style.css", "/three-style.css");
        Server.SetRedirect("/three-style.css", "/four-style.css");
        Server.SetRoute("/four-style.css",
            async context => { await context.Response.WriteAsync("body {box-sizing: border-box; }"); });

        var response = await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
        StringAssert.Contains("one-style.html", response.Url);
        Assert.AreEqual(5, requests.Count);
        Assert.AreEqual(ResourceType.Document, requests[0].ResourceType);
        Assert.AreEqual(ResourceType.StyleSheet, requests[1].ResourceType);

        // Check redirect chain
        var redirectChain = requests[1].RedirectChain;
        Assert.AreEqual(3, redirectChain.Length);
        StringAssert.Contains("one-style.css", redirectChain[0].Url);
        StringAssert.Contains("three-style.css", redirectChain[2].Url);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should be able to abort redirects")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldBeAbleToAbortRedirects()
    {
        await Page.SetRequestInterceptionAsync(true);
        Server.SetRedirect("/non-existing.json", "/non-existing-2.json");
        Server.SetRedirect("/non-existing-2.json", "/simple.html");
        Page.AddRequestInterceptor(request =>
        {
            if (request.Url.Contains("non-existing-2"))
            {
                return request.AbortAsync(RequestAbortErrorCode.Failed, 0);
            }

            return request.ContinueAsync(new Payload(), 0);
        });

        await Page.GoToAsync(TestConstants.EmptyPage);
        var result = await Page.EvaluateFunctionAsync<string>(@"async () => {
                try
                {
                    await fetch('/non-existing.json');
                }
                catch (e)
                {
                    return e.message;
                }
            }");

        if (TestConstants.IsChrome)
        {
            StringAssert.Contains("Failed to fetch", result);
        }
        else
        {
            StringAssert.Contains("NetworkError", result);
        }
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with equal requests")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithEqualRequests()
    {
        await Page.GoToAsync(TestConstants.EmptyPage);
        var responseCount = 1;
        Server.SetRoute("/zzz", context => context.Response.WriteAsync(((responseCount++) * 11) + string.Empty));
        await Page.SetRequestInterceptionAsync(true);

        var spinner = false;
        // Cancel 2nd request.
        Page.AddRequestInterceptor(request =>
        {
            if (TestUtils.IsFavicon(request))
            {
                return request.ContinueAsync(new Payload(), 0);
            }

            if (spinner)
            {
                spinner = !spinner;
                return request.AbortAsync(RequestAbortErrorCode.Failed, 0);
            }

            spinner = !spinner;
            return request.ContinueAsync(new Payload(), 0);
        });

        var results = await Page.EvaluateExpressionAsync<string[]>(@"Promise.all([
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
            ])");
        Assert.AreEqual(new[] { "11", "FAILED", "22" }, results);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should navigate to dataURL and fire dataURL requests")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldNavigateToDataURLAndFireDataURLRequests()
    {
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(request =>
        {
            requests.Add(request);
            return request.ContinueAsync(new Payload(), 0);
        });

        var dataURL = "data:text/html,<div>yo</div>";
        var response = await Page.GoToAsync(dataURL);
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
        Assert.That(requests, Has.Exactly(1).Items);
        Assert.AreEqual(dataURL, requests[0].Url);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should be able to fetch dataURL and fire dataURL requests")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldBeAbleToFetchDataURLAndFireDataURLRequests()
    {
        await Page.GoToAsync(TestConstants.EmptyPage);
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(request =>
        {
            requests.Add(request);
            return request.ContinueAsync(new Payload(), 0);
        });
        var dataURL = "data:text/html,<div>yo</div>";
        var text = await Page.EvaluateFunctionAsync<string>("url => fetch(url).then(r => r.text())", dataURL);

        Assert.AreEqual("<div>yo</div>", text);
        Assert.That(requests, Has.Exactly(1).Items);
        Assert.AreEqual(dataURL, requests[0].Url);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should navigate to URL with hash and fire requests without hash")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldNavigateToURLWithHashAndAndFireRequestsWithoutHash()
    {
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(request =>
        {
            requests.Add(request);
            return request.ContinueAsync(new Payload(), 0);
        });
        var response = await Page.GoToAsync(TestConstants.EmptyPage + "#hash");
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
        Assert.AreEqual(TestConstants.EmptyPage, response.Url);
        Assert.That(requests, Has.Exactly(1).Items);
        Assert.AreEqual(TestConstants.EmptyPage, requests[0].Url);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with encoded server")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithEncodedServer()
    {
        // The requestWillBeSent will report encoded URL, whereas interception will
        // report URL as-is. @see crbug.com/759388
        await Page.SetRequestInterceptionAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));
        var response = await Page.GoToAsync(TestConstants.ServerUrl + "/some nonexisting page");
        Assert.AreEqual(HttpStatusCode.NotFound, response.Status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with badly encoded server")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithBadlyEncodedServer()
    {
        await Page.SetRequestInterceptionAsync(true);
        Server.SetRoute("/malformed?rnd=%911", _ => Task.CompletedTask);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));
        var response = await Page.GoToAsync(TestConstants.ServerUrl + "/malformed?rnd=%911");
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with encoded server - 2")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithEncodedServerNegative2()
    {
        // The requestWillBeSent will report URL as-is, whereas interception will
        // report encoded URL for stylesheet. @see crbug.com/759388
        await Page.SetRequestInterceptionAsync(true);
        var requests = new List<IRequest>();
        Page.AddRequestInterceptor(request =>
        {
            requests.Add(request);
            return request.ContinueAsync(new Payload(), 0);
        });
        var response =
            await Page.GoToAsync(
                $"data:text/html,<link rel=\"stylesheet\" href=\"{TestConstants.ServerUrl}/fonts?helvetica|arial\"/>");
        Assert.AreEqual(HttpStatusCode.OK, response.Status);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(HttpStatusCode.NotFound, requests[1].Response.Status);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should not throw \"Invalid Interception Id\" if the request was cancelled")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldNotThrowInvalidInterceptionIdIfTheRequestWasCancelled()
    {
        await Page.SetContentAsync("<iframe></iframe>");
        await Page.SetRequestInterceptionAsync(true);
        IRequest request = null;
        var requestIntercepted = new TaskCompletionSource<bool>();
        Page.Request += (_, e) =>
        {
            request = e.Request;
            requestIntercepted.SetResult(true);
        };

        var _ = Page.QuerySelectorAsync("iframe")
            .EvaluateFunctionAsync<object>("(frame, url) => frame.src = url", TestConstants.ServerUrl);
        // Wait for request interception.
        await requestIntercepted.Task;
        // Delete frame to cause request to be canceled.
        _ = Page.QuerySelectorAsync("iframe").EvaluateFunctionAsync<object>("frame => frame.remove()");
        await request.ContinueAsync(new Payload(), 0);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should throw if interception is not enabled")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldThrowIfInterceptionIsNotEnabled()
    {
        Exception exception = null;
        Page.AddRequestInterceptor(async request =>
        {
            try
            {
                await request.ContinueAsync(new Payload(), 0);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        await Page.GoToAsync(TestConstants.EmptyPage);
        StringAssert.Contains("Request Interception is not enabled", exception.Message);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should work with file URLs")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldWorkWithFileURLs()
    {
        await Page.SetRequestInterceptionAsync(true);
        var urls = new List<string>();
        Page.AddRequestInterceptor(request =>
        {
            urls.Add(request.Url.Split('/').Last());
            return request.ContinueAsync(new Payload(), 0);
        });

        var uri = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "one-style.html")).AbsoluteUri;
        await Page.GoToAsync(uri);
        Assert.AreEqual(2, urls.Count);
        Assert.Contains("one-style.html", urls);
        Assert.Contains("one-style.css", urls);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should not cache if cache disabled")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldNotCacheIfCacheDisabled()
    {
        await Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style.html");
        await Page.SetRequestInterceptionAsync(true);
        await Page.SetCacheEnabledAsync(false);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));

        var cached = new List<IRequest>();
        Page.RequestServedFromCache += (_, e) => cached.Add(e.Request);

        await Page.ReloadAsync();
        Assert.IsEmpty(cached);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should cache if cache enabled")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldNotCacheIfCacheEnabled()
    {
        await Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style.html");
        await Page.SetRequestInterceptionAsync(true);
        await Page.SetCacheEnabledAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));

        var cached = new List<IRequest>();
        Page.RequestServedFromCache += (_, e) => cached.Add(e.Request);

        await Page.ReloadAsync();
        Assert.That(cached, Has.Exactly(1).Items);
    }

    [PuppeteerTest("requestinterception-experimental.spec.ts", "Page.setRequestInterception",
        "should load fonts if cache enabled")]
    [Skip(SkipAttribute.Targets.Firefox)]
    public async Task ShouldLoadFontsIfCacheEnabled()
    {
        await Page.SetRequestInterceptionAsync(true);
        await Page.SetCacheEnabledAsync(true);
        Page.AddRequestInterceptor(request => request.ContinueAsync(new Payload(), 0));

        var waitTask = Page.WaitForResponseAsync(response => response.Url.EndsWith("/one-style.woff"));
        await Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style-font.html");
        await waitTask;
    }
}
