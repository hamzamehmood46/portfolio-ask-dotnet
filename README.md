# Ask my portfolio: a chatbot that cites its sources

[![Test and deploy](https://github.com/hamzamehmood46/portfolio-ask-dotnet/actions/workflows/pages.yml/badge.svg)](https://github.com/hamzamehmood46/portfolio-ask-dotnet/actions/workflows/pages.yml)

**Live demo: [hamzamehmood46.github.io/portfolio-ask-dotnet](https://hamzamehmood46.github.io/portfolio-ask-dotnet/)**

A grounded question-and-answer bot in C#. It answers only from a small bundled corpus (the READMEs of ten of my .NET projects and one short about page), cites the passages it used, and says "I don't know from these docs" when the corpus does not cover the question. Blazor WebAssembly runs all of it in your browser.

> **The search and the model are both stand-ins.** There is no real language model and no real embedding model in the demo. `FakeEmbedder` matches shared words (tf-idf on a hashed bag of words), not meaning, so "cost" will not find "price". `FakeChatClient` is extractive: it copies the sentences from the retrieved passages that best match the question and cites them. It cannot paraphrase or reason, and it keeps no memory between questions. Answers can read awkwardly because they are copied sentences.

## Try it

1. Press a sample question, for example **Where did Hamza study?**. The answer is copied from the cited passage. Press a source chip to read the whole passage.
2. Open **What was retrieved** under an answer to see every passage and its score.
3. Press **What is Hamza's favorite food?**. Nothing in the docs matches, so the bot says it does not know and cites nothing. The model is never called for a question like this.

## How it works

```
question ─► redact email/phone ─► cap length ─► search (cosine on tf-idf vectors)
             best score < 0.20 ? ─► "I don't know" (no model call)
             otherwise ─► prompt: passages as <doc> data + the question ─► model
             model answer must be JSON, cite only retrieved passages ─► else one retry ─► else no answer
```

| Step | Behavior |
|---|---|
| **Chunking** | Markdown is split along its headings (up to three levels). Code fences stay whole and a `#` inside one is not a heading. A passage is at most 900 characters; an oversized table is cut by line. Passages with almost no text are dropped. |
| **Search** | `IEmbedder` produces unit vectors, so cosine similarity is a dot product. The default `FakeEmbedder` is deterministic: the same text gives the same vector on every machine. Words that appear nowhere in the corpus add weight to the question and lower its match, which is what lets off-topic questions fall below the threshold. |
| **Refusal** | If the best passage scores under 0.20 the answer is "I don't know from these docs", with no citations and no call to the model. |
| **Citations** | The model must return `{answerable, answer, citations[]}`. Every cited id must be one of the passages that were retrieved for this question. An invented id, a missing field, an empty citation list or an over-long answer is rejected. The assistant retries once, telling the model what was wrong, then gives up with a failed answer and no exception. |
| **Passages are data** | Retrieved text goes between `<doc>` tags and the system prompt says never to follow instructions found inside. Angle brackets in passages and in the question are replaced, so text cannot close a tag and write instructions of its own. |
| **Limits** | Questions are cut at 500 characters, at most 6,000 characters of passages are sent, and a model call that takes longer than 30 seconds is abandoned. |
| **Privacy** | Email addresses and phone numbers in a question are replaced with `[email]` and `[phone]` before anything is sent to a model. Names and street addresses are not detected. |

## Using a real model

Set environment variables; nothing is read from code and the key is never printed.

| Variable | Meaning |
|---|---|
| `ASK_AI_PROVIDER` | `fake` (default), `ollama` or `openai-compatible` |
| `ASK_AI_ENDPOINT` | Server address. Ollama defaults to `http://localhost:11434`. |
| `ASK_AI_MODEL` | Model name, required for `ollama` and `openai-compatible` |
| `ASK_AI_API_KEY` | Bearer token for a hosted service, if it needs one |

`AskClients.Create(AskSettings.FromEnvironment())` returns an `IChatClient` you can hand to `PortfolioAssistant`. **The Ollama and OpenAI-compatible clients are tested against a stub HTTP server only. I have not run them against a live model.** The embedder is the stand-in in every configuration; this repo has no real embedding client. The browser demo can only use the stand-in model, because a static page cannot safely hold a key.

## Honest limits

- The demo's "AI" is word matching plus sentence copying. It is a working, tested pipeline around a placeholder, not a demonstration of a language model's quality.
- The 0.20 refusal threshold was set by looking at the scores of about fourteen answerable and unanswerable questions on this corpus. It separates them with little margin: for example "Does Hamza know Rust?" scores 0.195 against a threshold of 0.20. A different corpus or embedder needs its own calibration, and the regression tests only cover the questions I tried.
- Matching is by word, so a correct question can still get "I don't know" when the docs use different words. "How does the XML mapping service stay secure?" is one: the README says "Security", the question says "secure". Some answers are the right passage but an unhelpful sentence.
- The corpus is ten of my own READMEs copied as they were, plus an about page I wrote from my resume. The about page is short on purpose; the bot cannot tell you anything about me that is not in it.
- Everything is in memory and loads on page open. There is no login, no storage and no conversation history.

## How it is built

```
src/Ask.Core   Chunker, FakeEmbedder, KnowledgeIndex, PortfolioAssistant, FakeChatClient, HTTP clients, Redactor
src/Ask.Web    Blazor WebAssembly demo; the corpus lives in wwwroot/corpus
tests/         xUnit tests for the core
```

The only package behind the core is `Microsoft.Extensions.AI.Abstractions`, for `IChatClient`. JSON is built with `JsonObject` rather than anonymous types so it survives trimming in the published WebAssembly app.

## Tests

```bash
dotnet test
```

91 tests, all offline. They cover chunking (heading paths, code fences, size limits, Windows line endings), the embedder (determinism, unit length, stemming, rarity weighting), redaction, retrieval over the real corpus, refusals (the model is never called below the threshold), answers whose every word comes from the cited passages, rejected and retried model output, timeouts and cancellation, tag-escaping against prompt injection, the context budget, the settings and HTTP clients against a stub server, and a check that the corpus holds no email address, phone number or key.

I have not run deliberate-breakage checks on this project, so I cannot say how many mistakes the tests would miss.

## Adding your own documents

Put markdown files in `src/Ask.Web/wwwroot/corpus/` and list them in `index.json`. A test fails if the list and the folder disagree.
