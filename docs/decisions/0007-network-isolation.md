# 0007 — Network isolation: native 403 for non-disc requests, CSP as meta tag

**Status:** accepted (M2)

§10 asks for the CSP both as a `<meta>` tag and as a launcher response header. WebView2 does not raise
`WebResourceRequested` for requests served by `SetVirtualHostNameToFolderMapping` (verified: the event fires for
`https://unmapped.invalid/*` but never for `https://disc.local/*`), so the launcher cannot add headers to disc pages
without serving every file itself — including byte ranges for video seeking.

Instead the launcher registers a `*` filter and answers **every** request that reaches the event — by construction
everything outside disc.local — with 403. That is stronger than `connect-src`/`img-src`: no page, user template or
injected script can reach the network. The builder writes the CSP (`DiscSecurity.ContentSecurityPolicy`) as a meta
tag into every page it renders, which still governs inline-script and object rules.
Verified by the CDP probe: an external image fails, `https://disc.local/...` loads.
