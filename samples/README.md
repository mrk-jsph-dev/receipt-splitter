# Samples

`sample-receipt.json` is the bundled sample used by "Try the sample receipt": the Ramen Nagi
receipt (9 lines, 20 units, ₱5,255.80) with 12 made-up names. The API embeds it at build time
(`src/ReceiptSplitter.Api/ReceiptSplitter.Api.csproj`), so any change here ships with the next API release.

It holds receipt lines and made-up names only. Never add real receipt photos or real names here.
