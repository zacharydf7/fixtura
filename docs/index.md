---
_layout: landing
title: Fixtura
---

<div class="fx-home">

<section class="fx-hero fx-full">
<div class="fx-wrap">
<div>
<div class="fx-eyebrow"><b>New</b> For ASP.NET Core on .NET 10 and xUnit v3</div>
<h1>Integration tests for ASP.NET Core, <span>composed, not inherited</span></h1>
<p class="fx-lede">Describe each shape of test host once, from small features. Fixtura swaps services and databases,
signs users in through your app's real authentication, fakes the APIs it calls, and resets data between tests, so no
test repeats setup.</p>
<div class="fx-actions">
<a class="fx-btn fx-btn-primary" href="getting-started.md">Get started <i class="bi bi-arrow-right" aria-hidden="true"></i></a>
<a class="fx-btn fx-btn-ghost" href="tutorials/test-a-secured-api.md">Tutorials</a>
<a class="fx-btn fx-btn-ghost" href="https://github.com/zacharydf7/fixtura"><i class="bi bi-github" aria-hidden="true"></i> GitHub</a>
</div>
<div class="fx-install">$ <b>dotnet add package</b> Fixtura</div>
</div>
<div class="fx-code">
<div class="fx-code-bar"><i></i><i></i><i></i><span>CheckoutTests.cs</span></div>

[!code-csharp[](samples/Bookshop.Tests/Landing.cs#landing)]

</div>
</div>
</section>

<section class="fx-section">
<div class="fx-wrap">
<div class="fx-section-head">
<span class="fx-kicker">Why Fixtura</span>
<h2>The test setup every team writes by hand, done once</h2>
<p>Each feature replaces code that usually lives in a fixture base class, copied from project to project.</p>
</div>
<div class="fx-grid">
<a class="fx-card fx-card-featured" href="guides/test-users.md">
<h3><i class="bi bi-person-check" aria-hidden="true"></i>Real sign-in, not a stub</h3>
<p>Test users sign in through your app's own JWT bearer or cookie authentication, so its token events, security-stamp
checks and ASP.NET Identity store all run. No fake authentication handler that tests something your users never
see.</p>
</a>
<a class="fx-card" href="guides/service-swaps.md">
<h3><i class="bi bi-arrow-left-right" aria-hidden="true"></i>Swaps that fail loudly</h3>
<p>Replace services and DbContexts with one call, keeping the app's lifetimes. A renamed service fails the run
instead of testing the wrong thing.</p>
</a>
<a class="fx-card" href="guides/settings.md">
<h3><i class="bi bi-sliders" aria-hidden="true"></i>Settings as objects</h3>
<p>Override configuration with an object shaped like appsettings.json. Program.cs sees it before Build(), and
arrays replace the app's whole.</p>
</a>
<a class="fx-card" href="guides/sql-server.md">
<h3><i class="bi bi-database" aria-hidden="true"></i>A database per fixture</h3>
<p>SQL Server from LocalDB, a CI service or a container. Tests skip locally when there's no server and fail in CI,
with no skip call anywhere.</p>
</a>
<a class="fx-card" href="guides/resetting-data.md">
<h3><i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Resets in milliseconds</h3>
<p>Empty every table in foreign-key order between tests and restart identities, keeping reference data and the
schema.</p>
</a>
<a class="fx-card" href="guides/http-fakes.md">
<h3><i class="bi bi-cloud-arrow-down" aria-hidden="true"></i>Outbound HTTP, faked</h3>
<p>Answer the app's HttpClient calls in-process, check what it sent, and block anything you forgot to fake.</p>
</a>
<a class="fx-card" href="guides/logs.md">
<h3><i class="bi bi-terminal" aria-hidden="true"></i>Logs where you look</h3>
<p>The app's logs land in the output of the test that caused them, so a failing 500 comes with its reason.</p>
</a>
<a class="fx-card" href="guides/fixtures-and-features.md">
<h3><i class="bi bi-puzzle" aria-hidden="true"></i>Features that compose</h3>
<p>Write shared setup once as a feature and add it to any fixture. Adding a feature twice runs it once.</p>
</a>
</div>
</div>
</section>

<section class="fx-band fx-full">
<div class="fx-section fx-wrap">
<div class="fx-section-head">
<span class="fx-kicker">Documentation</span>
<h2>Find your way</h2>
<p>Start with the tutorials, then dip into the guides when you need the details.</p>
</div>
<div class="fx-paths">
<div class="fx-path">
<h3><i class="bi bi-rocket-takeoff" aria-hidden="true"></i>Start here</h3>
<ul>
<li><a href="getting-started.md">Getting started</a></li>
<li><a href="tutorials/test-a-secured-api.md">Test a secured API</a></li>
<li><a href="tutorials/test-against-sql-server.md">Test against SQL Server</a></li>
<li><a href="tutorials/fake-a-partner-api.md">Fake a partner API</a></li>
</ul>
</div>
<div class="fx-path">
<h3><i class="bi bi-boxes" aria-hidden="true"></i>Building hosts</h3>
<ul>
<li><a href="guides/fixtures-and-features.md">Fixtures and features</a></li>
<li><a href="guides/service-swaps.md">Swapping services</a></li>
<li><a href="guides/dbcontext-swaps.md">Swapping DbContexts</a></li>
<li><a href="guides/settings.md">Settings</a></li>
</ul>
</div>
<div class="fx-path">
<h3><i class="bi bi-people" aria-hidden="true"></i>Users and the outside world</h3>
<ul>
<li><a href="guides/test-users.md">Test users</a></li>
<li><a href="guides/http-fakes.md">Faking outbound HTTP</a></li>
<li><a href="guides/logs.md">The app's logs</a></li>
<li><a href="guides/kestrel-and-overrides.md">Kestrel and per-test overrides</a></li>
</ul>
</div>
<div class="fx-path">
<h3><i class="bi bi-database" aria-hidden="true"></i>Data and running tests</h3>
<ul>
<li><a href="guides/sql-server.md">SQL Server</a></li>
<li><a href="guides/resetting-data.md">Resetting data</a></li>
<li><a href="guides/ci.md">Running in CI</a></li>
<li><a href="guides/troubleshooting.md">Troubleshooting</a></li>
</ul>
</div>
</div>
</div>
</section>

<section class="fx-section">
<div class="fx-wrap">
<div class="fx-section-head">
<span class="fx-kicker">Packages</span>
<h2>Take only what your app uses</h2>
<p>Every type lives in the <code>Fixtura</code> namespace, so tests need one using, whichever packages they add.</p>
</div>
<div class="fx-packages">
<div class="fx-package">
<a href="https://www.nuget.org/packages/Fixtura">Fixtura</a>
<p>The core: fixtures, features, swaps, settings, test users, HTTP fakes, logs and resets.</p>
</div>
<div class="fx-package">
<a href="https://www.nuget.org/packages/Fixtura.EntityFrameworkCore">Fixtura.EntityFrameworkCore</a>
<p>Point any registered DbContext at a test database, whatever its registration shape.</p>
</div>
<div class="fx-package">
<a href="https://www.nuget.org/packages/Fixtura.SqlServer">Fixtura.SqlServer</a>
<p>A SQL Server database per fixture, with fast resets between tests.</p>
</div>
<div class="fx-package">
<a href="https://www.nuget.org/packages/Fixtura.SqlServer.Testcontainers">Fixtura.SqlServer.Testcontainers</a>
<p>The test SQL Server in a Docker container, one per test run.</p>
</div>
</div>
</div>
</section>

<section class="fx-cta fx-full">
<div class="fx-wrap">
<h2>Your first test in five minutes</h2>
<p>Add the package, write a fixture, and run <code>dotnet test</code>.</p>
<div class="fx-actions">
<a class="fx-btn fx-btn-primary" href="getting-started.md">Get started <i class="bi bi-arrow-right" aria-hidden="true"></i></a>
<a class="fx-btn fx-btn-ghost" href="api/index.md">API reference</a>
</div>
</div>
</section>

</div>
