# Twilio Messaging Console: send, receive and track texts

[![Test and deploy](https://github.com/hamzamehmood46/twilio-messaging-dotnet/actions/workflows/pages.yml/badge.svg)](https://github.com/hamzamehmood46/twilio-messaging-dotnet/actions/workflows/pages.yml)

**Live demo: [hamzamehmood46.github.io/twilio-messaging-dotnet](https://hamzamehmood46.github.io/twilio-messaging-dotnet/)**

A messaging console in C# that follows the rules real SMS providers enforce. You can compose texts, send to lists, read replies in an inbox, and watch every message go from queued to delivered (or fail with a carrier-style error code). Blazor WebAssembly runs the same engine in your browser.

> **Nothing is ever sent.** The "carrier" is a simulator, so the public demo cannot text anyone or use anyone's Twilio credits. The rules and the shapes (message ids, statuses, error codes, webhook signatures) follow Twilio's, so the engine could sit behind a real Twilio client; that client is not included.

## Try it (3 minutes)

1. **Compose:** type a message and watch the part counter. Add an emoji and the whole text switches to a smaller limit and more parts.
2. Send to **Cy (landline)** or **Eli (flagged as spam)**, then press **+10 seconds**: they fail with error codes. Ava's delivers.
3. **Inbox:** open a conversation and press **Reply STOP** as that person. They are blocked from then on, the standard confirmation goes out, and sending to them is refused with error 21610.
4. **Bulk:** preview a send to the whole list. People who opted out, or who are missing a name, are skipped with the reason, before anything is sent.
5. **Account:** switch to a trial account (prefix on every message, verified numbers only) or turn on quiet hours.

## What it does

| Capability | Behavior |
|---|---|
| **Phone numbers** | Typed numbers become E.164 (`(555) 123-4567` becomes `+15551234567`). Bad numbers are refused with Twilio's code 21211. |
| **Message parts** | Counts parts the way carriers bill them: standard text fits 160 characters in one part and 153 per part once split; one emoji or a non-standard character (such as ş) switches the whole message to 70 and 67; a few symbols (`€`, `{`, `[`) count double. |
| **Delivery lifecycle** | queued, sending, sent, delivered, or undelivered with an error code (30006 landline, 30007 filtered as spam, 30003 unreachable). Messages leave the queue at the sender's rate of one per second, oldest first. Every status change is kept in the history. |
| **Status callbacks** | Status updates can only move forward, so a late or repeated callback is harmless and a finished message never changes. |
| **Opt-outs** | STOP-type words (`STOP`, `UNSUBSCRIBE`, `CANCEL`, `END`, `QUIT` and more) block the number, even if it is not a contact, and get the standard confirmation. START-type words unblock it. HELP gets the help text. Only a message that is exactly the keyword counts, so "please stop" is just a reply. |
| **Trial account** | Only verified numbers can be texted, and a prefix is added to every message that also counts toward the parts. |
| **Quiet hours** | No texts before 08:00 or from 21:00 in the contact's own time zone. |
| **Templates and bulk** | `{first_name}`, `{name}`, `{phone}`. A placeholder that cannot be filled is an error, never sent as literal text. A bulk send is previewed first, works once, and expires after 10 minutes. Someone who opts out between the preview and the send is still skipped. |
| **Inbox** | Conversations grouped by number, unread counts, threads in time order with each message's status. |
| **Webhook signatures** | Verifies the `X-Twilio-Signature` header (the URL plus each parameter name and value in name order, HMAC-SHA1, base64) with a constant-time comparison. |

## How it is built

```
src/Sms.Engine   C#: numbers, part counting, keywords, templates, signatures, the messenger and the simulated carrier. No UI.
src/Sms.Web      Blazor WebAssembly front end (runs the same engine in the browser)
tests/           xUnit tests
```

The clock is injected, so every time rule is tested by moving a simulated clock.

## Tests

```bash
dotnet test
```

165 tests, including:

- phone number formats and every kind of bad input;
- part counting at every boundary (160 and 161, 306 and 307, 70 and 71 for special characters, extension characters, emoji);
- keyword matching: case, spacing, and near misses that must not count;
- the webhook signature against a value computed by hand from the documented recipe, parameter order, and any change to the body, URL or token;
- the send rules with their error codes, trial prefix and its effect on the part count, and quiet hours at each hour boundary and across time zones;
- the delivery lifecycle to the second, each carrier failure, the send rate, and forward-only callbacks;
- STOP, START and HELP, opt-outs for numbers that are not contacts, and the bulk preview and confirm rules;
- a randomized test: 400 random actions across six seeds, after which every message's history must be in order and end at its status, error codes must match failures, and nothing but an automatic confirmation may go to a number after it said STOP.

I have not run deliberate-rule-breaking checks on this project.

## Scope

Data lives in memory, so reloading resets the demo. There is no real Twilio connection, no database and no login. The cost shown is an illustrative per-part price, not a quote.
