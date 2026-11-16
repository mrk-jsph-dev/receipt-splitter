# Receipt Splitter spec

> Source of truth for building Receipt Splitter. Rendered version with mockups: `docs/design/spec.html`.
> Clickable screens: `docs/design/walkthrough.html`. Build order and CLI prompts: `docs/EXECUTION_PLAN.md`.
> Decided since the first draft: .NET 10 minimal API on Azure Container Apps; one manual `ci-cd.yml`; no CodeQL.


Turning the single-purpose Ramen Nagi splitter into a reusable bill splitter: snap or type a receipt, check what was read, share a link, friends claim their items, and everyone sees who owes whom.

Source: mrk-jsph-dev/ramen-nagi-bill @ 0797ae0 Target: near-$0/month on Azure No accounts

## What is hardcoded today

The current app is well built for its one job. The maths engine, the op-based updates with conflict checks, ETag retries, polling and the tap-to-assign dialog are worth keeping. What ties it to one dinner is that the receipt, the people and the fees live in source code.

| Where                                   | What is fixed                                                                                          | Change                                                      |
|-----------------------------------------|--------------------------------------------------------------------------------------------------------|-------------------------------------------------------------|
| `shared/bill.ts`                        | 12 named people, 9 receipt lines, 40% discount, ₱680.80 service charge, ₱5,255.80 total, one `BILL_ID` | Move all of it into bill data created at runtime            |
| `shared/bill.ts`                        | Discount always proportional, service charge always equal, no tax or tip                               | Generic adjustments list with a chosen allocation rule each |
| `formatPeso`                            | ₱ and 2 decimals only                                                                                  | Per-bill ISO 4217 currency with its own minor-unit digits   |
| `PaymentQr.tsx`, `my_qr.png`            | One person's InstaPay QR, full name and bank                                                           | Per-bill "how to pay me" note, set by the organizer         |
| `ReceiptItemsPanel.tsx`, `BillPage.tsx` | "Ramen Nagi", "SM Megamall", date, receipt image import                                                | Merchant, date and items come from the bill                 |
| `api/src/core.ts`                       | Single bill, auto-created on first GET                                                                 | Create bills explicitly, each with an unguessable id        |
| `api/src/functions/bills.ts`            | Storage reached with a connection string                                                               | Managed identity, no stored secret                          |
| Repo root                               | Real receipt photo (cashier and server names, invoice number), personal QR, coworkers' full names      | Remove before the repo goes public, including from history  |

## Feature list

MVP ships first. Next follows once the MVP is live. Later is out of scope until someone asks for it.

| Feature                               | What it does                                                                                                                                                                                                                                                                                                  | Today                    | Phase                            |
|---------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------|----------------------------------|
| **Many separate bills**               | Anyone can start any number of bills. Each has its own unguessable link and storage, and nothing from one bill shows in another (section 9).                                                                                                                                                                  | One bill                 | MVP                              |
| **Activity and payment confirmation** | See who claimed what. "I've paid" waits for the receiver to confirm.                                                                                                                                                                                                                                          | Missing                  | MVP                              |
| **Sample receipt**                    | "Try the sample" builds a bill from a bundled JSON receipt. No AI call, no upload.                                                                                                                                                                                                                            | Missing                  | MVP                              |
| **Manual entry**                      | Type items, quantities and prices. Works when scanning is capped or unavailable.                                                                                                                                                                                                                              | Missing                  | MVP                              |
| **Receipt scan**                      | Take a photo or pick one from the gallery (or upload on desktop), then draft items with Azure AI Document Intelligence. Daily cap and per-device rate limit.                                                                                                                                                                                                                | Missing                  | MVP                              |
| **Several receipts in one bill**      | Dinner, then drinks, then dessert in one link. Each receipt keeps its own fees, its own payer and its own list of who was there. One settle-up nets across all of them. Long receipts can be scanned as two photos.                                                                                           | Missing                  | MVP                              |
| **Review before share**               | Every read line is editable. Low-confidence lines are flagged. Sharing stays locked until the computed total matches the receipt total the organizer confirms.                                                                                                                                                | Missing                  | MVP                              |
| **Add people**                        | The organizer types names, taps friends from earlier bills on this phone, or shows a QR so each person joins and adds themselves. Anyone can add a friend who has no phone. A printed headcount (Ramen Nagi prints "HeadCount 12") is shown as a hint. Rename at any time; remove only people with no claims. | 12 fixed names           | MVP                              |
| **Per-unit items**                    | A line of 8 × Butao King becomes 8 claimable units.                                                                                                                                                                                                                                                           | Yes                      | MVP                              |
| **Shared items**                      | Several people claim one unit (a platter, a pitcher). Split equally by default.                                                                                                                                                                                                                               | Missing                  | MVP                              |
| **Senior and PWD discounts**          | Mark who holds a Senior Citizen or PWD card. The printed SC/PWD discount and VAT exemption go to that person only, in every split mode, and are never spread over the group.                                                                                                                                  | Missing                  | MVP                              |
| **Split modes**                       | Per receipt: by item (claim what you had), equally, or by shares. Custom amounts next. Milk tea for four can be "equally" while dinner is "by item".                                                                                                                                                          | By item only             | MVP · Next amounts               |
| **Uneven shares**                     | Weights on a shared item, e.g. 2 : 1 : 1.                                                                                                                                                                                                                                                                     | Missing                  | Next                             |
| **Tax**                               | Added tax (US sales tax) or tax already in prices (PH VAT). Proportional by default.                                                                                                                                                                                                                          | Missing                  | MVP                              |
| **Service charge**                    | Read from the receipt or typed in. Split by what each ordered (default) or equally.                                                                                                                                                                                                                           | Equal only               | MVP                              |
| **Tip**                               | Percent helper or fixed amount, equal or proportional.                                                                                                                                                                                                                                                        | Missing                  | MVP                              |
| **Discounts**                         | Percent or fixed amount, any label (card promo, voucher, senior/PWD). Applies to the whole receipt, chosen items, or chosen people. Split by what each ordered or equally.                                                                                                                                    | One fixed 40%            | MVP bill-wide · Next item-scoped |
| **Exact rounding**                    | Largest-remainder allocation so totals always match to the smallest coin.                                                                                                                                                                                                                                     | Yes, service charge only | MVP                              |
| **Currency per bill**                 | ISO 4217 code with its own decimals (PHP 2, JPY 0, KWD 3) and locale formatting.                                                                                                                                                                                                                              | ₱ only                   | MVP                              |
| **Who paid the restaurant**           | One or more payers with the amount each put down.                                                                                                                                                                                                                                                             | Implied                  | MVP one payer · Next several     |
| **Settle-up**                         | Fewest transfers from net balances, each with a "paid" toggle.                                                                                                                                                                                                                                                | Paid toggle only         | MVP                              |
| **Share link and QR**                 | Unguessable link, QR on screen for the table, copy button.                                                                                                                                                                                                                                                    | One fixed URL            | MVP                              |
| **Payment note**                      | Free text such as "GCash 09xx… / BPI …" shown on every card.                                                                                                                                                                                                                                                  | One personal QR          | MVP                              |
| **Copy summary**                      | Plain-text "who owes what" to paste in a group chat.                                                                                                                                                                                                                                                          | Missing                  | Next                             |
| **Recent bills and friends**          | Bills you opened and names you used, kept in this browser only and suggested when adding people.                                                                                                                                                                                                              | Missing                  | Next                             |
| **Auto-delete**                       | Bills delete 30 days after the last change; organizer can delete now.                                                                                                                                                                                                                                         | Kept forever             | MVP                              |
| **Lock bill and undo**                | Organizer undoes any claim, or freezes the bill once settled.                                                                                                                                                                                                                                                 | Missing                  | MVP                              |
| Currency conversion                   | Pay in a different currency from the receipt. Needs a rates source.                                                                                                                                                                                                                                           | —                        | Later                            |
| Groups and running balances           | Debts carried across many bills. Needs identity, which we avoid.                                                                                                                                                                                                                                              | —                        | Later                            |
| Payment integrations                  | Pay links into GCash, PayPal and so on.                                                                                                                                                                                                                                                                       | —                        | Later                            |
| Accounts, push, real time             | Kept out on purpose for cost and simplicity.                                                                                                                                                                                                                                                                  | —                        | Later                            |

## UI design

[Open the clickable end-to-end walkthrough](https://claude.ai/artifact/4SR3rM6twuBoUXJUywnKTZ) for all 28 screens in order, including an ending with a second receipt, from scan to settled.

Mobile first, because people claim at the table, and responsive at every size (see "Responsive layout" below). Graphite and deep teal, minimal and flat: white cards with hairline borders, one accent, Lucide icons. The responsive section below supersedes the older glass-card look in the walkthrough. Teal means yours, amber means still unclaimed, red means check this, green means paid. Receipt lines keep a mono face like the printed slip. Numbers use the bundled sample receipt and made-up names (Ana, Ben, Carla, Dan), and are worked out with the rules in section 6.

See `docs/design/walkthrough.html` for every screen, `docs/design/responsive.html` for tablet and desktop layouts, and `docs/design/spec.html` for the rendered mockups.

## Responsive layout

The app is one responsive site, not a phone layout framed on a big screen. It fits phones,
tablets, laptops and large monitors with the same routes, components and data. See
`docs/design/responsive.html` for Home, Bill (claim), Check receipt and Settle up at
390 px, 834 px and 1280 px.

### No accounts, and it shows

There is no sign-in, so nothing may look like a profile or an account.

- No greeting ("Hi Ana"), profile avatar, "Me" tab or notification bell.
- No money totals across bills ("You're owed ₱…"). Money is shown per bill only.
- No app-wide Settle or Activity. Both live inside each bill.
- Home is a start page ("Split a bill": scan, type in, try the sample) plus **On this device**,
  the bills this browser has opened, with a footnote: saved in this browser only, bills
  delete 30 days after the last change, and a **Forget this device** link.
- A name belongs to one bill: the bill header shows "You're Ben · Change".

### Look

Minimal and flat. Graphite neutrals with one deep teal accent, light and dark.

| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` | `#F6F7F7` | `#0D1112` | Page ground |
| `--surface` | `#FFFFFF` | `#151A1C` | Cards, bars |
| `--ink` | `#111618` | `#E9EEEF` | Text |
| `--muted` | `#6B777B` | `#8C989C` | Secondary text |
| `--line` | `#E3E7E8` | `#252C2F` | 1 px borders |
| `--accent` | `#0E6B6B` | `#4DBDB5` | Actions and "you" |
| `--amber` | `#9A6210` | `#E3B25A` | Unclaimed |
| `--ok` | `#1F7A55` | `#5CCB9B` | Paid |
| `--warn` | `#B4442B` | `#EE8B71` | Check this |

- Cards: white, 1 px border, 14 px radius, no shadow. Only the receipt card lifts.
- Avatars: neutral grey initials. The current person's avatar is teal.
- Type: Inter for everything (drop Sora), JetBrains Mono for receipt lines. Tabular figures
  for amounts.
- Icons: Lucide only, via `lucide-vue-next`, stroke 1.75, 20 px (16 px in chips and small
  buttons). No emoji, no filled icons, no second icon set. Icon-only buttons get an
  `aria-label`.
- Logo: a receipt outline with a peso sign (₱), drawn in the Lucide style as
  `web/src/design/icons/ReceiptPeso.vue` (Lucide's `receipt` path plus a ₱ inside; Lucide
  has no peso receipt). Never a dollar sign. Inside a bill, Receipts uses Lucide `receipt-text`.

### Breakpoints

Mobile-first: base CSS is the phone layout, and each size adds rules with `min-width`.

| Size | Width | Typical device | Home | Inside a bill | Sheets and dialogs |
|---|---|---|---|---|---|
| Compact | < 640 px | Phones, portrait | One column: start actions, then bills on this device | Tab bar at the bottom: Items, People, Receipts, Settle. Your share pinned above it | Bottom sheets |
| Medium | 640 to 1023 px | Tablets, phones in landscape, narrow windows | Two columns: start actions, bills | 76 px icon rail with the same four sections. Items and people left, summary pinned right | Centred dialog, max 480 px |
| Expanded | ≥ 1024 px | Laptops, desktops | Start actions pinned left, bills two across | 240 px sidebar: home link, the four sections with counts, other bills on this device. Three columns: unclaimed · people · summary. Drag items onto people | Side drawer for breakdowns, dialog for edits |
| Wide | ≥ 1240 px | Large monitors | Content stops at 1120 px and centres | Same as expanded | Same |

Home has no navigation bar at any size: it is one page. Nothing has a fixed 480 px width.

### Scan: camera or gallery

The scan card offers two ways in, so people can use a receipt photo they already took:

- Phones and tablets: **Take photo** (`<input type="file" accept="image/*" capture="environment">`)
  and **From gallery** (`<input type="file" accept="image/*,application/pdf" multiple>`, no
  `capture`, so the OS shows the photo library and files).
- Laptops and desktops: **Upload photos** plus a drop zone for dragged files.
- Accepted: JPEG, PNG, HEIC/HEIF, TIFF, BMP and PDF, up to 4 MB each after client-side resize.
  Checked against the Document Intelligence v4.0 docs (November 2026): the receipt model reads
  those formats, F0 refuses files over 4 MB and reads only the first 2 pages of a PDF.
  JPEG and PNG photos are scaled down in the browser; HEIC and PDF are sent as they are when they fit.
- Several files picked at once: each becomes its own receipt, then the "new receipt or next
  page?" question lets the organizer join a long receipt's pages.
- Every file counts once toward the daily scan cap. Files are read once and never stored,
  whether they came from the camera or the gallery.

### Input and accessibility rules

- Every tap target is at least 44 × 44 px at every size.
- Drag and drop only under `(pointer: fine)` and the expanded size. Mine and Share always work.
- Hover styles only under `(hover: hover)`.
- Every action works with Tab, Enter and Escape. The focus ring is always visible.
- Text runs at most about 70 characters wide. Amounts never wrap. Long names and shop names
  truncate with an ellipsis and show in full in the detail view.
- Fixed bars add `env(safe-area-inset-*)` so notches and home bars never cover them.
- `prefers-reduced-motion` turns off sheet and drawer slide animations.

### How it is built

- `BillShell.vue` owns the bill's tab bar, icon rail and sidebar, switched by CSS only.
  Home renders without it.
- Breakpoints live once in `web/src/design/breakpoints.css`; a small `useBreakpoint()`
  composable exists only for behaviour CSS can't do (sheet vs drawer, drag and drop on or off).
- One `Overlay.vue` renders as a bottom sheet, a centred dialog or a side drawer by size.
- `HeroBand` is retired. Welcome and Your name use the same flat cards.
- No separate mobile and desktop pages, no CSS framework, no UI library besides the icons.

### Checks

Playwright screenshots at 360, 390, 768, 1024, 1280 and 1600 px, light and dark, for each
built screen. At every width: no horizontal page scroll, no clipped text, and the primary
action on screen without scrolling. Rotate a phone to landscape: medium layout. Browser zoom
at 200 %: still usable.

## User flows

### Organizer

1.  First visit only: welcome screen, then "What should we call you?". The name is saved in this browser, not an account, and becomes the organizer's name on new bills. Skippable.
2.  Start: scan, sample, or type in.
3.  Scan only: take a photo or pick one or more from the gallery (upload or drag in on desktop). Each file is sent once, read, and dropped. Draft lines come back.
4.  Review: fix lines, set currency, add tax, tip, service and discounts as printed, pick how each is split, confirm the receipt total.
5.  Optional: "Add another receipt" (scan, sample or type in) and repeat the review. For each receipt, set who paid it and who was there.
6.  Share is enabled only when every receipt's computed total = its confirmed total.
7.  Add people: type names, tap friends from earlier bills, or let them join by link or QR.
8.  Name the bill (suggested from the first receipt, editable any time), add a payment note and share.
9.  Watch claims fill in, nudge on unclaimed items, mark payments.

### Friend

1.  Open the link. Pick "I'm…" from the list or add a name. Choice is kept on this device.
2.  Tap "Mine" for a unit, or "Share" to split it with others.
3.  See your share update with a full breakdown.
4.  Pay using the note, then tap "I've paid".

### Two people, same item

Each change carries what the person saw (`from`). If Carla claimed the unit first, Ben's request fails with a conflict, his screen reloads, and he sees "Carla claimed this a moment ago." Nothing is overwritten silently. This is the current app's approach, kept as is.

### Organizer edits after sharing

Editing a line's price keeps its claims. Deleting a line removes its claims and shows a notice to the people affected. Changing the confirmed total re-locks sharing until it matches again.

## Data model

One JSON document per bill. Money is always an integer count of the currency's minor unit (centavos for PHP, yen for JPY). That is exact decimal arithmetic without floating point.

    type Minor = number;            // integer minor units; never a float
    type Id = string;               // short random id, unique within the bill

    interface Bill {
      id: string;                   // 128-bit random, base62 (22 chars)
      schema: 1;
      version: number;              // +1 on every write
      createdAt: string; updatedAt: string;
      title: string;                // organizer's choice, e.g. "Team lunch"; suggested from the first receipt's merchant, editable any time
      currency: { code: string; digits: 0 | 2 | 3 };   // ISO 4217
      status: "draft" | "shared" | "locked";
      people: Person[];             // order = added order; used for tie-breaks
      headcountHint?: number;       // printed headcount, shown when adding people
      receipts: Receipt[];          // one or more; maths runs per receipt
      paymentNote?: string;         // free text, max 280 chars
      payments: { personId: Id; status: "waiting" | "confirmed" }[];
      activity: { id: Id; at: string; kind: string; text: string; personId?: Id; unitId?: Id }[];   // last 100; id and unitId let the organizer undo a claim
      lockedAt?: string;
      organizerKeyHash: string;     // SHA-256 of the organizer key
    }

    interface Receipt {
      id: Id;
      label: string;                // "Dinner", "Milk tea"
      merchant?: string; receiptDate?: string;
      source: "sample" | "manual" | "scan";
      photos: number;               // pages scanned for this receipt (long receipts)
      confirmedTotal: Minor;        // organizer-confirmed printed total
      lines: Line[];                // as printed
      adjustments: Adjustment[];    // apply to THIS receipt only
      splitMode: "items" | "equal" | "shares" | "amounts";
      shares?: Record;  // splitMode "shares": e.g. a couple counts as 2
      amounts?: Record;  // splitMode "amounts" (Next): must sum to confirmedTotal
      participants: Id[];           // who was there; equal splits use this list
      payers: { personId: Id; amount: Minor }[];   // who paid this receipt
    }

    interface Person {
      id: Id; name: string /* max 40 */;
      card?: "senior" | "pwd";      // set by the organizer; the restaurant checks the actual ID
    }

    interface Line {
      id: Id; name: string; quantity: number; unitPrice: Minor;
      lineTotal: Minor;             // as printed; must equal qty × unit unless edited
      confidence?: number;          // 0–1 from scan; < 0.8 is flagged
      units: Unit[];                // one per physical unit
    }

    interface Unit {
      id: Id;                       // "butao-3"
      claims: { personId: Id; weight: number /* 1 = equal */ }[];
    }

    interface Adjustment {
      id: Id;
      kind: "tax" | "tip" | "service" | "discount" | "fee" | "senior" | "pwd" | "vatExempt";
      label: string;                // as printed: "40% BDO OFF"
      amount: Minor;                // signed; discounts are negative
      rate?: number;                // percent helper, basis points (4000 = 40%); amount is the truth
      includedInPrices: boolean;    // true for VAT already in item prices (info only)
      split: "proportional" | "equal";   // by what each ordered, or same for everyone
      scope?: { units?: Id[]; people?: Id[] };   // absent = whole receipt
      source: "scan" | "manual";    // scan-detected ones are flagged until confirmed
    }

Percent fields are UI helpers that fill `amount`. The stored amount is what the receipt printed, so the base a restaurant used (Ramen Nagi charged service on the VAT-exclusive price) never has to be guessed.

## Splitting maths rules

### Allocation primitive: largest remainder

`allocate(total, weights[]) → Minor[]`. Each share is `floor(total × wᵢ / Σw)`. The leftover minor units go one each to the largest fractional parts; ties go to the earlier person in `people` order. The result always sums to `total`. Negative totals (discounts) are allocated as a positive amount and negated. Integer maths only; use 64-bit integers (`long`, with `checked` arithmetic) for the products.

1.  **Split mode, per receipt.**
    - **By item** (default): people claim what they had, then steps 2 to 8 below.
    - **Equally**: no claiming. `allocate(confirmedTotal, [1 per participant])`. Fees and discounts are already inside the total, so nothing else is allocated.
    - **By shares**: `allocate(confirmedTotal, shares)`, e.g. a couple counts as 2, a kid as 0.5.
    - **Custom amounts** (Next): the organizer types each amount; saving is blocked until they add up to the total.
    - **Person-scoped discounts in Equally and By shares** (senior, PWD, or any discount limited to chosen people): take them out first, split the rest, then give each one back only to its people. `base = confirmedTotal − Σ person-scoped`, `allocate(base, weights)`, then add each person-scoped amount to its people.
2.  **Per receipt.** Steps 2 to 7 run separately for each receipt, using only that receipt's lines, adjustments and participants. Receipt 1's service charge never touches receipt 2's items. A person's bill total is the sum of their totals across receipts.
3.  **Units.** A line's `lineTotal` is spread over its units with `allocate(lineTotal, [1,1,…])`, so a printed 3 for 100.00 becomes 33.34, 33.33, 33.33.
4.  **Shared units.** A unit's price is spread over its claimants with `allocate(price, weights)`.
5.  **Food subtotal.** Each person's sum of unit shares. Unclaimed units go to a virtual **Unclaimed** bucket that sits last in the order.
6.  **Proportional adjustments** (the default for every kind): `allocate(amount, subtotals in scope)` across people plus Unclaimed.
7.  **Equal adjustments** (chosen per fee, e.g. the Ramen Nagi group split service equally): `allocate(amount, [1 per participant])`, where participants are the people ticked as "was there" for that receipt (everyone by default). Participants with no items still pay their share, as in the current app.
8.  **Tax included in prices** adds nothing; it is shown as information only.
9.  **Person total** = food subtotal + Σ adjustment shares.
10. **Invariant**, checked per receipt in code and tests: Σ person totals + Unclaimed = Σ lineTotal + Σ adjustments = that receipt's `confirmedTotal`. Sharing stays off until every receipt holds it.
11. **Settle-up across receipts.** Net = Σ paid on all receipts − Σ owed on all receipts. Netting happens once for the whole bill, so if Ana paid dinner and Ben paid milk tea, nobody sends money both ways. Repeatedly match the largest debtor with the largest creditor, transfer the smaller of the two, ties by people order. This gives at most n−1 transfers. A true minimum is an NP-hard problem and not worth it at table size.

### Fees and discounts: what the organizer can set

| Setting       | Options                                                                                                                                         | Example                                        |
|---------------|-------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------|
| Kind          | Discount, service charge, tax, tip, other fee                                                                                                   | "40% BDO OFF" is a discount                    |
| Amount        | Percent of the items it applies to, or a fixed amount. The printed amount wins if they differ by more than one centavo, and the app shows both. | 40% of 7,625.00 = 3,050.00                     |
| Applies to    | Whole receipt, chosen items, or chosen people's items                                                                                           | Senior discount on Lola's items only           |
| Split         | By what each ordered, or equally among who was there                                                                                            | Ramen Nagi: discount by order, service equally |
| Tax in prices | Added on top, or already included (shown only)                                                                                                  | PH VAT is already in menu prices               |

### Senior citizen and PWD discounts (Philippines)

Restaurants apply these to the card holder's own share. On a VAT-inclusive bill the usual computation removes the 12% VAT from the holder's share and then takes 20% off. For a shared group meal, the holder's share is the total divided by the number of diners. \[VERIFY AGAINST CURRENT RA 9994 / RA 10754 RULES AND BIR GUIDANCE\] The app does not recompute any of this. It uses the amounts the restaurant printed and only decides who gets them.

- The scan looks for lines such as `SC DISC`, `SENIOR`, `PWD DISC`, `VAT EXEMPT`, `LESS VAT`. They become `senior`, `pwd` and `vatExempt` adjustments, flagged for review.
- The organizer picks who they belong to (people with a card). Those adjustments get `scope.people` = the card holders and are never split across others.
- By item: the holder's discount sits against their own claims. If the discount is larger than what they claimed, the app warns, since the restaurant may have assumed a group meal.
- Two card holders: each line is assigned to its holder; one line covering both is split between them by their shares of the base.

### Worked example: group meal with one senior

Four people share ₱2,240.00 of food (VAT included), split equally. Lola has a senior card. The receipt prints the share and discounts as the restaurant computed them.

| Receipt line                                    | Amount       | How the app treats it |
|-------------------------------------------------|--------------|-----------------------|
| Food subtotal                                   | 2,240.00     | Split equally among 4 |
| Less VAT, senior share (560.00 ÷ 1.12 = 500.00) | −60.00       | Lola only             |
| SC DISC 20% of 500.00                           | −100.00      | Lola only             |
| **Total due**                                   | **2,080.00** |                       |

Base = 2,080.00 − (−160.00) = 2,240.00, so each person's equal share is 560.00. Lola pays 560.00 − 160.00 = **400.00**. The other three pay **560.00** each. 400.00 + 3 × 560.00 = 2,080.00. Spreading the discount over everyone would have made it 520.00 each, which takes Lola's benefit away from her.

### How a scan finds fees and discounts

- Document Intelligence's receipt model (`prebuilt-receipt`, API 2024-11-30) returns `MerchantName`, `TransactionDate`, `Total`, `Subtotal`, `TotalTax`, `Tip` and `Items` (each with `Description`, `Quantity`, `Price`, `TotalPrice`), plus fields we don't read (phone, address, time, `TaxDetails`, `CountryRegion`, `ReceiptType`). Checked against the v4.0 docs in November 2026. Service charges and discounts are not standard fields, so they come back as ordinary lines or not at all.
- `TotalTax` counts on top only when `Subtotal + TotalTax (+ Tip) = Total`; otherwise it is shown as already in the prices. Lines read with confidence below 0.8 are flagged "Not sure about this price".
- Saving a receipt in the editor counts as reviewing it: its scanned fees stop being flagged.
- Our parser moves lines into fees and discounts by their words and sign: `service charge`, `svc`, `s/c` → service; `discount`, `% off`, `less`, `promo`, negative or bracketed amounts → discount; `vat`, `tax` → tax. A percent in the label fills the rate.
- Every detected fee shows up flagged: "We read this as a service charge. Right?" The organizer can change its kind, amount and split, or turn it back into an item.
- Anything missed is added by hand. The total check catches it, because the receipt will not match until it is in.

### Worked example: the sample receipt

The bundled sample is the Ramen Nagi receipt with made-up names. With 12 people, a 40% discount split by item share and the service charge split equally:

| Person (order) | Items                                                                     | Food     | Discount  | Service | Owes         |
|----------------|---------------------------------------------------------------------------|----------|-----------|---------|--------------|
| Ben (2)        | Butao King, ⅓ Gyoza (shared with Fay and Gio; Ben gets the extra centavo) | 548.34   | −219.34   | 56.74   | 385.74       |
| Carla (3)      | Gyu King                                                                  | 795.00   | −318.00   | 56.74   | 533.74       |
| Dan (4)        | Red King, Coke Zero                                                       | 710.00   | −284.00   | 56.74   | 482.74       |
| Eli (5)        | Butao King                                                                | 485.00   | −194.00   | 56.73   | 347.73       |
| **Whole bill** |                                                                           | 7,625.00 | −3,050.00 | 680.80  | **5,255.80** |

Ben's discount is 3,050.00 × 548.34 ⁄ 7,625.00 = 219.336, shown rounded. The exact centavo depends on the largest-remainder pass across all 12 people, which tests pin down.

### Worked example: two receipts

After dinner, Ana, Ben, Carla and Dan get milk tea: 4 × 150.00 = ₱600.00, no fees, paid by Ben. Only those four are ticked as "was there".

| Person    | Dinner owed | Milk tea owed | Paid     | Net       |
|-----------|-------------|---------------|----------|-----------|
| Ana       | 404.74      | 150.00        | 5,255.80 | +4,701.06 |
| Ben       | 385.74      | 150.00        | 600.00   | +64.26    |
| Carla     | 533.74      | 150.00        | 0.00     | −683.74   |
| Dan       | 482.74      | 150.00        | 0.00     | −632.74   |
| 8 others  | 3,448.84    | —             | 0.00     | −3,448.84 |
| **Total** | 5,255.80    | 600.00        | 5,855.80 | 0.00      |

Ben paid more than he owes overall, so he sends nothing and gets 64.26 back from one person. Without netting, he would send 385.74 to Ana while Ana, Carla and Dan each send him 150.00.

### Tests the engine must have

- The current Ramen Nagi suite, re-expressed as data: 20 units, ₱5,255.80, 56.74 × 4 + 56.73 × 8.
- Allocation sums exactly for random totals and weights (property test, 10,000 cases).
- JPY (0 digits) and KWD (3 digits) bills.
- Shared unit with weights 2 : 1 : 1, and a 3-for-100.00 line.
- Added tax plus tip plus negative discount reconcile with unclaimed items present.
- Settle-up with two payers: transfers ≤ n−1, everyone's net reaches 0.
- Split modes: equal and shares modes sum exactly to the receipt total for 1 to 50 people; switching modes keeps claims but ignores them.
- Senior/PWD: the worked example above gives 400.00 and 3 × 560.00; the discount never lowers anyone else's amount, in every split mode.
- Two receipts: receipt 1's service charge and discount never change receipt 2 amounts, and equal splits use only that receipt's participants.
- Determinism: same bill in, same numbers out, regardless of claim order.

## API

JSON over HTTPS. No accounts. The bill id in the link grants claim access. The organizer key (returned once on create, kept in the organizer's browser, stored only as a hash) grants editing.

| Method and path                                 | Who       | Does                                                                                                                                                                                                                                                                                                                                                                                    |
|-------------------------------------------------|-----------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `POST /api/bills`                               | anyone    | Body `{ source: "sample" | "manual", draft? }`. Returns `{ id, organizerKey }` once. Rate limited per IP, plus a daily cap across all clients (`429` with `{ reason: "daily-cap" }`). There is no route that lists bills.                                                                                                                                                                                                                                           |
| `POST /api/scans`                               | anyone    | Multipart form: `file`, `currencyCode`, `currencyDigits`, optional `previous` (the draft of the first photo, for `append`). Image or PDF from the camera or the gallery (JPEG/PNG/HEIC/TIFF/BMP/PDF, ≤ 4 MB, type checked by its first bytes). Checks daily cap and per-IP limit, reads with Document Intelligence, returns draft lines, adjustments, total and confidences. Optional `append: true` joins a second photo of the same long receipt to the previous draft. Each photo counts once toward the cap. The image is never stored. `429` with `{ reason: "daily-cap" | "rate" }` when limited. |
| `GET /api/scans/quota`                          | anyone    | `{ remainingToday }` for the start screen.                                                                                                                                                                                                                                                                                                                                              |
| `GET /api/bills/{id}`                           | link      | Returns the bill with an `ETag`. Honors `If-None-Match` and answers `304` when nothing changed, which keeps polling cheap.                                                                                                                                                                                                                                                              |
| `PATCH /api/bills/{id}`                         | link      | One op: `addPerson`, `join` (add yourself and remember it on this device), `rename`, `removePerson` (no claims only), `claim`, `unclaim`, `setWeight`, `markPaid` (sets waiting). Each carries `from`; mismatch returns `409` with the latest bill.                                                                                                                                     |
| `POST /api/bills/{id}/receipts`                 | organizer | Add a receipt from a scan draft, the sample or manual entry. Max 10 receipts per bill.                                                                                                                                                                                                                                                                                                  |
| `PUT /api/bills/{id}/receipts/{rid}` · `DELETE` | organizer | Replace a receipt's lines, adjustments, participants, payers and confirmed total, or remove it (its claims go with it). Header `X-Organizer-Key` and `If-Match`.                                                                                                                                                                                                                        |
| `POST /api/bills/{id}/receipts/check`          | organizer | Body `{ draft, receiptId? }`. The total check while a receipt is typed or fixed: line totals, Σ items, Σ adjustments, difference, matches, the amount each percent comes to, and the receipt breakdown. Saves nothing. |
| `PUT /api/bills/{id}/settings`                  | organizer | Title, currency and payment note for the whole bill.                                                                                                                                                                                                                                                                                                                                    |
| `POST /api/bills/{id}/share` · `/lock`          | organizer | Move status. Share fails with `422` unless the invariant holds.                                                                                                                                                                                                                                                                                                                         |
| `POST /api/bills/{id}/confirm` · `/undo`        | organizer | Confirm a payment, or undo a claim by activity entry.                                                                                                                                                                                                                                                                                                                                   |
| `DELETE /api/bills/{id}`                        | organizer | Delete now.                                                                                                                                                                                                                                                                                                                                                                             |

Details settled while building the API (module 2):

- All money on the wire is integer minor units. Responses are `{ bill, breakdown }`; `breakdown` is null until the bill has at least one person.
- Organizer calls send the key in `X-Organizer-Key` only. A missing or wrong key is `403`. Receipt replace and delete and settings need `If-Match`: missing is `428`, stale is `412` with the latest bill.
- `from` mismatches and other "the bill moved on" refusals are `409` with the latest bill. Bad input, including an id from another bill, is `400` and writes nothing. A locked bill refuses every write except delete.
- A receipt replace keeps claims on lines sent with their `id`, up to the new quantity. Claims on units that went away are removed with a notice in the activity list. An organizer edit that leaves a shared bill not matching its totals moves it back to draft.
- `confirm` also works without "I've paid" first, for cash. `POST /decline` ("Not yet") sends a payment back to unpaid. `undo` takes `{ activityId }` of a claim entry.
- `setCard` (PATCH) marks a senior or PWD card holder. Sharing is refused with `empty-receipt` while any receipt has no lines.
- Shares in "shares" mode accept up to 2 decimals (a kid as 0.5). Claim weights are whole numbers from 1 to 100.

Polling: every 10 s while the tab is visible, stopped when hidden, slowed to 30 s after 5 minutes without local changes. Writes retry on ETag mismatch with jittered backoff, as today.

## Architecture, deployment, cost and privacy

    Browser (Vue + TypeScript + Vite)    — display only, no money maths
      │  static files
      ├─▶ Azure Static Web Apps — Free plan
      │  https://api.[DOMAIN]/…  (CORS: the SWA origin only)
      └─▶ Azure Container Apps — Consumption, scale 0 → 1
            .NET 10 minimal API (ASP.NET Core), one container, system-assigned managed identity
            ├─▶ Blob Storage (LRS)   bills/{id}.json        If-Match ETags, 30-day lifecycle delete
            ├─▶ Blob Storage (same)  limits/{date}/{ipHash}.json   2-day lifecycle delete
            └─▶ Azure AI Document Intelligence — F0, prebuilt receipt model
          RBAC only: Storage Blob Data Contributor, Cognitive Services User.
          No keys or connection strings anywhere. Image pulled from GitHub Container Registry (private, read:packages token kept only as a Container App secret).

### Why Container Apps for a minimal API

A minimal API is an ordinary ASP.NET Core app, so it needs a host that runs a web server. Container Apps on the Consumption plan scales to zero when nobody is using it, supports managed identity, and has a monthly free grant \[VERIFY CURRENT GRANT\]. Functions would force the Functions programming model instead of a minimal API, and App Service Free has a daily CPU quota and no scale to zero.

### Why the server owns the maths

With a .NET API there is one maths engine, in C#, using `long` minor units and `decimal` only for parsing and display. Every `GET` and `PATCH` returns the computed breakdown, so all phones show the same numbers. The browser shows a claim instantly and the totals arrive in the same response, typically well under a second.

### Cold starts

After about 5 idle minutes the API scales to zero. The first request after that waits a few seconds while the container starts; the page shows "Waking up…". While anyone has a bill open, 10-second polling keeps it warm.

### Project layout

`src/ReceiptSplitter.Core` (money maths, models, no I/O), `src/ReceiptSplitter.Api` (.NET 10 minimal API, `Program.cs` + endpoint groups), `tests/ReceiptSplitter.Core.Tests` and `tests/ReceiptSplitter.Api.Tests` (xUnit: maths, isolation, security), `web/` (Vue), `infra/` (Bicep), `samples/`, `.github/workflows/ci-cd.yml`.

### Why Blob for bills

Bills grow with items and people, and a Table property caps at 64 KB. A blob per bill has the same ETag concurrency, no size worry, and a lifecycle rule ("delete 30 days after last modified") does retention with no timer code.

### Cost guards

| Item                         | Guard                                                                                                                         | Possible charge                                                                                                                  |
|------------------------------|-------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------|
| Static Web Apps              | Free plan                                                                                                                     | None on Free; bandwidth limits apply                                                                                             |
| Container Apps (Consumption) | Min replicas 0, max 1, 0.25 vCPU / 0.5 GiB, no Log Analytics workspace (no logs destination). \[VERIFY\] Application Insights now needs a Log Analytics workspace, so it is off | Above the monthly free grant \[VERIFY CURRENT GRANT\]. A 2-hour dinner with polling keeps one small replica awake about 2 hours. |
| Container registry           | GitHub Container Registry, private image, pulled with a read-only token stored as a Container App secret                      | None. Azure Container Registry Basic would be a fixed monthly charge, so it is not used.                                         |
| Document Intelligence        | F0 tier, daily cap `SCAN_DAILY_CAP` = 15, 3 scans per IP per hour                                                             | None on F0; F0 refuses beyond its monthly page limit \[VERIFY F0 LIMIT\]. 15/day keeps a 31-day month at 465.                    |
| Storage (LRS)                | Small JSON, 304 polling, 30-day delete                                                                                        | Cents per month at most                                                                                                          |
| Application Insights         | Not deployed: it would need a Log Analytics workspace \[VERIFY\]                                                                | None                                                                                                                             |
| Budget alert                 | Azure Cost Management budget \[AMOUNT, e.g. $1\] with email alert                                                            | Catches surprises                                                                                                                |

### Deployment plan

Everything is created by Bicep files kept in the repo under `infra/`: `main.bicep` (entry point), `modules/` (storage, docintel, containerapp, staticwebapp, roles, budget) and `main.bicepparam` (region, names, budget amount; no secrets). GitHub signs in to Azure with OpenID Connect (a federated credential), so no Azure password or key is stored in GitHub.

**One manual pipeline:** `.github/workflows/ci-cd.yml`, triggered only by `workflow_dispatch`. Nothing runs on push or pull request. It has one input, `target`: `test`, `infra`, `api`, `web` or `all`. The `test` job always runs first, and every deploy job `needs: test`, so nothing ships with failing tests. Dependency caching, a 15-minute timeout and cancel-in-progress keep runs short. Start it from the Actions tab or with `gh workflow run ci-cd.yml -f target=api`.

    ci-cd.yml   (on: workflow_dispatch, input target)
      test     always       dotnet test · web build + tests · dotnet list package --vulnerable · npm audit
      infra    target ∈ {infra, all}   needs test   az deployment group create (Bicep)
      api      target ∈ {api, all}     needs test   build + push GHCR image, update Container App, wait for /healthz
      web      target ∈ {web, all}     needs test   build web, deploy to SWA
      smoke    after any deploy        sample bill: create, claim, check totals, delete

| Step               | What happens                                                                                                                                                                                                                                                                                                  | Who                                       |
|--------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------|
| 1\. One-time setup | Create a resource group in \[REGION, e.g. Southeast Asia\]. Create an Entra app registration with a federated credential for the repo's `main` branch and the `production` environment, and give it Contributor on the resource group plus User Access Administrator limited to assigning the two data roles. | Mark, once, with a script from the README |
| 2\. Infrastructure | `az deployment group create` with `infra/main.bicep`: storage account (LRS, no shared-key access, lifecycle rules), Document Intelligence F0, Container Apps environment and app (managed identity, min 0 / max 1), Static Web App Free, role assignments, budget alert.                                      | `target=infra`                            |
| 3\. Tests          | `dotnet test` (maths, isolation, security), web build and tests, plus the free vulnerability checks built into `dotnet` and `npm`. No CodeQL. Run with `target=test` on any branch when you want a check without deploying.                                                                                   | Always the first job                      |
| 4\. API release    | From `main`: build the container, push `ghcr.io/[OWNER]/receipt-splitter-api:<commit>`, then deploy `infra/main.bicep` again with that tag, so port, probes, settings and roles stay in one place. The new revision must pass `/healthz` before it takes traffic.                                                                                                             | `target=api`                              |
| 5\. Web release    | Build `web/` and deploy to SWA. The deploy token is fetched at run time after the OIDC sign-in, so it is never stored as a secret.                                                                                                                                                                            | `target=web`                              |
| 6\. Smoke test     | Create a sample bill, claim one item, check the totals, delete the bill.                                                                                                                                                                                                                                      | After any deploy                          |
| Rollback           | Point the Container App back at the previous image tag (one command, kept in `infra/README.md`). For the web app, re-run the previous commit's deploy.                                                                                                                                                           | Mark or Claude                            |

Production only, no separate staging and no automatic PR previews. The API is checked by the manual test run plus the health-gated revision. Production deploys need an approval on the GitHub `production` environment. Custom domain: \[DOMAIN\], optional; the default `*.azurestaticapps.net` and `*.azurecontainerapps.io` addresses work at no cost.

### Privacy

- Receipt images are held in memory for one request, sent to Document Intelligence, and dropped. They are never written to our storage.
- Only item names, prices, adjustments, merchant, date and total are kept from a scan. Raw text, card digits and staff names are discarded.
- Document Intelligence's own retention of analyze results \[VERIFY IN MICROSOFT DOCS\] is stated in the README.
- Bills, including people's names and payment notes, delete 30 days after the last change. The organizer can delete sooner.
- Pages send `noindex` and `no-referrer`, as today, so links don't leak to search engines or other sites.
- The sample receipt ships as JSON with made-up names, not as the original photo.

## Many bills, nothing leaking between them

Anyone can use the app for any number of bills. Each bill is sealed off: a person sees a bill only if they hold its link, and nothing from one bill (names, items, amounts, payment notes) can show up in another. There are no accounts, so the rules below do the work an account system would.

### 1 · Separate storage per bill

- One blob per bill: `bills/{id}.json`. No shared index, no list of bills, no search.
- The API reads and writes only the exact id in the URL. There is no endpoint that returns more than one bill.
- Every op is checked against that bill's own people and units, so an id from bill A is rejected in bill B.

### 2 · Links that can't be guessed

- Bill id: 128 random bits from a crypto RNG, 22 base62 characters. Never a counter, date or name.
- Unknown, deleted and expired bills all return the same `404`, so probing learns nothing.
- More than 20 not-found lookups from one IP in 10 minutes gets `429`, so ids can't be brute-forced.
- "One IP" for every per-IP limit means one IPv4 address or one IPv6 /64, since a single connection usually gets a whole /64.

### 3 · Two levels of access

- **Claim link** (`/b/{id}`): view, join, claim, add people, say "I've paid".
- **Organizer key**: a second 128-bit secret, sent once on create, kept in the organizer's browser, stored only as a SHA-256 hash and compared in constant time. Needed to edit the receipt, confirm payments, undo claims, lock or delete.
- **Organizer link** (`/b/{id}#key`): the key travels after `#`, which browsers never send to servers or put in referrers. The app offers "Save organizer link" for moving to another phone.
- Responses never include the key hash or any server field. The client gets exactly what its access level allows.

### 4 · Nothing carries over on the device

- A new bill starts empty. People from earlier bills are only suggestions, read from this browser, and are never sent to the server unless tapped.
- Local storage is keyed per bill: `bill:{id}:me`, `bill:{id}:key`. Opening bill B never reads bill A's entries.
- "Forget this device" in settings clears recent bills, friends, names and organizer keys, for shared or borrowed phones.

### 5 · Guards inside a bill

- **Activity list**: "Ben claimed Kakuni Belly · 6:20 PM". The last 100 events are kept inside the bill and deleted with it.
- **Payment needs both sides**: "I've paid" sets *waiting*, and only the receiver (organizer key) sets *confirmed*. Totals count confirmed payments only.
- **Organizer undo and lock**: undo any claim, or lock the bill so it can only be viewed.

### 6 · No leaks outside the bill

- Logs and Application Insights record a salted hash of the bill id, never the id, names or amounts. Request bodies are not logged.
- Headers: `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, `X-Robots-Tag: noindex`, strict CSP with no third-party scripts, HTTPS only, CORS limited to the app's own origin.
- Scan results go back only to the person who scanned and are not saved until they create the bill.
- Rate-limit counters hold a salted hash of the IP, not the IP, and expire after 2 days.

**What a link can't protect against.** Anyone the link is forwarded to can claim items in that bill. That is the trade-off of no accounts. The activity list, payment confirmation, undo and lock make mistakes or mischief visible and fixable. If a group needs more, passkey sign-in can be added later without changing how bills are stored.

### Security tests (in CI, alongside the maths tests)

- An op on bill A that names a person or unit from bill B is rejected and bill B is unchanged.
- Edit, confirm, undo, lock and delete without the organizer key, or with another bill's key, return `403`.
- Unknown, deleted and expired ids give identical `404` bodies. Not-found lookups past the limit give `429`.
- No API response contains `organizerKeyHash`, and no route lists bills.
- Ids and keys come from the crypto RNG and are 22 base62 characters (property test).
- After creating bill B on the same browser, bill B's state contains nothing from bill A.

## Open decisions and placeholders

| Question                     | Recommendation                                                                                                                                                                                                                                                                         |
|------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| API language                 | **Decided: .NET 10 minimal API** on Container Apps. The server owns the maths, so there is still exactly one engine. The current TypeScript engine and its tests are ported to C# and xUnit first, and both must agree on the Ramen Nagi fixture before the TypeScript one is removed. |
| New repo or evolve this one? | New public repo seeded from this one without the photo, QR and names in history.                                                                                                                                                                                                       |
| Domain                       | \[DOMAIN\] SWA's default `*.azurestaticapps.net` works at no cost.                                                                                                                                                                                                                     |
| Scan cap                     | 15 per day, 3 per IP per hour, both settings.                                                                                                                                                                                                                                          |
| Retention                    | 30 days after the last change.                                                                                                                                                                                                                                                         |
| API limits                   | \[PLACEHOLDER\] Defaults in `appsettings.json` until confirmed: 10 new bills per IP per hour, 500 new bills per day in total \[VERIFY\], 60 writes and 120 bill reads per IP per minute, 256 KB request body, 50 people per bill, 200 lines per receipt, 100 units per line, 100 weight per claim, 100,000,000 minor units per line or adjustment. |
| App name                     | \[APP NAME\] "Receipt Splitter" used here as a working name.                                                                                                                                                                                                                           |

## Build order

One small module at a time, each with tests and CI green before the next starts, and a plan or mockup shown first.

1.  **Maths engine (C#).** Generic `Allocate`, per-receipt totals, split modes, person-scoped discounts, settle-up. xUnit tests including the Ramen Nagi fixture, the senior example and property tests. CI on every PR.
2.  **Bill data and API (.NET 10 minimal API).** Create/get/patch on Blob with ETags, organizer key, sample bill, the isolation rules and security tests in section 9. Managed identity. Bicep and the deploy pipeline below.
3.  **Claim screen.** Current UI driven by bill data: dynamic people, shared units, currency formatting.
4.  **Review and manual entry.** Line editor, adjustments, confirmed-total lock.
5.  **Share and settle-up.** Link, QR, payment note, transfers, paid toggles.
6.  **Scan.** Document Intelligence, quota, rate limit, privacy notes in README.
7.  **Demo and launch.** Short screen recording with the sample, budget alert, README.
