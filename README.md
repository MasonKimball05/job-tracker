# Job Tracker

A job-application tracker with Claude built in. Paste a job posting and it
fills in the details. It compares postings against your résumé and drafts
tailored cover letters. Built with **C# / .NET 10**, **Blazor** (interactive
server), **Entity Framework Core + SQLite**, and the official **Anthropic C# SDK**.

## Features

- **Board:** Saved → Applied → Interviewing → Offer, plus closed jobs, response
  rate, and a banner for follow-ups that are due
- **Filters & sorting:** search, work mode (in person / hybrid / remote),
  location, minimum match score; sort by best match, follow-up date, company,
  or recent. Filters are kept in the URL, so refreshes and bookmarks keep them
- **Add from a posting:** Claude extracts company, role, location, work mode,
  salary (only if stated, never guessed), requirements and nice-to-haves
- **Résumé match:** a 0–100 score with strengths, gaps, and honest
  suggestions. Upload your résumé as a PDF (Claude reads it directly) or paste text
- **Cover letters:** stream in live, can be stopped mid-draft, are editable, and
  never claim experience your résumé doesn't show
- **Contacts and notes** per application

## Run

Requires the .NET 10 SDK (`brew install dotnet`).

```bash
cd src/JobTracker
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."   # optional: enables AI features
dotnet run                                                # → http://localhost:5206
```

Without a key everything works except the ✨ buttons, which explain how to add one.
The key can also come from the `ANTHROPIC_API_KEY` environment variable.

```bash
dotnet test        # from the repo root
```

## How the Claude features work

All three live in [`Services/JobAi.cs`](src/JobTracker/Services/JobAi.cs) behind an
`IJobAi` interface:

| Feature | Technique |
|---|---|
| Posting extraction | Structured outputs: a JSON schema guarantees parseable output ([`AiModels.cs`](src/JobTracker/Services/AiModels.cs)) |
| Match analysis | Structured outputs; the résumé PDF is sent as a document block |
| Cover letter | Streaming, rendered into the textarea chunk by chunk |

Model: `claude-haiku-4-5`, the cheapest Claude model: each feature costs roughly a cent or two per use. To trade cost for quality, change the `Model` constant in `JobAi.cs` (e.g. `claude-opus-5-5`).

**Safety:**
- Job postings are untrusted third-party text. The system prompt tells Claude to treat
  them as data and ignore any instructions inside them.
- Output is always rendered as text, never as HTML.
- Every response's stop reason is checked before its content is used.

## Privacy

- Data is stored in `~/Library/Application Support/JobTracker/jobs.db`, outside the repo
  (set `JOBTRACKER_DB` to use a different file).
- The API key lives in .NET user-secrets in your home folder, never in the repo.
- Postings and your résumé leave your machine only when you click a ✨ button.
- The app listens on localhost only.

## Layout

```
src/JobTracker/
  Data/Models.cs            entities: JobApplication, Contact, Resume
  Data/JobDbContext.cs      EF Core context, JSON list columns, DB location
  Data/Migrations/          generated schema migrations
  Services/JobAi.cs         Claude calls
  Services/AiModels.cs      result records + JSON schemas + parsing
  Services/BoardView.cs     board grouping and stats
  Components/Pages/         Board, NewJob, JobDetail, ResumePage
  Components/Shared/        JobFields form, JobCard, ScorePill
tests/JobTracker.Tests/     xUnit: parsing, schemas, board logic, EF round-trips
```

## Ideas

- Drag-and-drop between board columns
- Interview prep: likely questions for a posting, answered from your résumé
- Import a posting straight from a URL
- Weekly summary: what's due, what's gone quiet
