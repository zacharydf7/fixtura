# Browser tests

The `Fixtura.Playwright` package runs the app on Kestrel and opens its pages in a real browser, already signed in as a
test user:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#playwright)]

## The browser

`host.AddPlaywright()` switches the fixture to `ServerKind.Kestrel`, where a browser can reach the app, and launches the
browser once, when the host is built. `PlaywrightOptions` sets:

| Option | What it does |
| --- | --- |
| `Browser` | `PlaywrightBrowser.Chromium` unless set, or `Firefox` or `WebKit` |
| `Headless` | `false` opens a window, to watch a test while debugging |
| `RequiredWhenVariableIsTrue` | An environment variable that, when true, makes a browser that isn't installed fail the fixture instead of skipping its tests |

Install the browser once, from the test project's build output:

```
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

On a Linux CI runner, add `--with-deps` for the libraries the browser needs. Without the browser, the fixture's tests
skip, saying how to install it; with the variable set, as in CI, they fail.

## Pages

- `api.OpenPageAsync(path, user)` signs the user in through the app's own authentication, as `CreateClientAsync` does:
  the app's cookie, added for the app's address and split as the app splits a large one, or a bearer token, sent as an
  `Authorization` header with the requests the page makes to the app, never to other sites. Without a user, the page
  is anonymous.
- Each page has a browser context of its own, so one test's cookies and storage never reach another's.
  `ResetDataAsync` closes the pages the tests opened, and the browser closes with the fixture.
- The page is Playwright's own `IPage`, so its locators, assertions and waits all work.
- Kestrel serves plain HTTP, so an app whose cookie is Secure stops receiving it once it renews it; see
  [Kestrel and per-test overrides](kestrel-and-overrides.md). Requests from the browser belong to no test, so their
  logs become diagnostic messages; see [The app's logs](logs.md).
