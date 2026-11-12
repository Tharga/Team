# Plan: support-gmail-quote

- [x] 1. NuGet check: nothing beyond the recorded holds.
- [x] 2. Nested quote in text and HTML, QuoteStyle option, existing snapshots updated, new snapshot tests, docs.
- [~] 3. Push for testing; on the users word close-out, PR, close #464.

## Release notes
An answer mail now quotes as Gmail does by default (one block, older entries nested). `SupportQuoteStyle.PerEntry` restores the old shape. The HTML carries gmail_quote markers.
