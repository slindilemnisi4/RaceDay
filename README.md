# RaceDay

RaceDay is a web-based event management platform for the South African road running, walking, and cycling community. Participants can discover and enter events, while organisers can create and manage events, categories, entries, and results.

## What you can do

### Participant

- Create an account and sign in
- Browse events and enter event categories
- View enrolments and personal results

### Organiser

- Create and manage events and categories
- View event entries
- Capture participant results
- Add event images and videos

Organiser accounts require an invitation code. The local development codes are listed below.

## Technology

- ASP.NET Core MVC web application and ASP.NET Core REST API
- .NET 10
- SQLite with Entity Framework Core migrations
- xUnit tests

SQL Server and SQL Server Management Studio are **not** required to run the current application.

## Requirements

- Windows, macOS, or Linux
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- A modern web browser

The application uses a local HTTPS development certificate. If you have not trusted one on your machine, run `dotnet dev-certs https --trust` and accept the operating system prompt. On Linux, follow the .NET documentation for configuring a trusted development certificate.

`ffprobe` is only needed for video upload duration validation. Install FFmpeg and make sure `ffprobe` is on your `PATH` if you want to test video uploads.

## Get the source

```powershell
git clone https://github.com/Slindile-04/RaceDay.git
cd RaceDay
```

## Run RaceDay locally

The API and web application run as separate processes. Open two terminals in the repository root.

1. In the first terminal, start the API using its HTTPS launch profile:

   ```powershell
   dotnet run --project .\src\RaceDay.API\RaceDay.API.csproj --launch-profile https
   ```

   The API listens at `https://localhost:7059` (and `http://localhost:5250`). The web app is configured to call the HTTPS API address.

2. In the second terminal, start the web application:

   ```powershell
   dotnet run --project .\src\RaceDay.Web\RaceDay.Web.csproj --launch-profile https
   ```

3. When the web app has started, copy **https://localhost:7265** into your preferred browser and press Enter. The HTTPS launch profile may open a browser automatically; you can also paste the address yourself.

If the browser displays a certificate warning, trust the .NET development HTTPS certificate as described under [Requirements](#requirements), then reload the page.

The API's Swagger UI is available at [https://localhost:7059/swagger](https://localhost:7059/swagger) while the API is running.

## Create an account

On the RaceDay site, select **Register**, enter your details, and choose a role:

- **Participant:** Register without an invitation code.
- **Organiser:** Choose Organiser and enter one of the local development invitation codes:

  | Invitation code |
  |---|
  | `6842` |
  | `1739` |
  | `5206` |
  | `9471` |
  | `3058` |

These codes are included for local testing and demonstration only. Do not use them to protect a production service. Organiser codes for the API are configured under `Registration:OrganiserCodes` in `src/RaceDay.API/appsettings.json`; use private configuration or environment variables for real deployments.

## Database

The API uses SQLite. Its connection string is `Data Source=RaceDay.db` in `src/RaceDay.API/appsettings.json`. Database migrations are **not** applied automatically when the API starts.

The repository includes `src/RaceDay.API/RaceDay.db` for local development. To create or update the database schema from the current migrations, install the EF Core command-line tool if needed:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
```

If `dotnet-ef` is already installed, update it instead:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.11
```

Then run the migration command from the repository root:

```powershell
dotnet ef database update --project .\src\RaceDay.API\RaceDay.API.csproj --startup-project .\src\RaceDay.API\RaceDay.API.csproj
```

Before applying migrations to a database with data you want to keep, stop the API and make a backup:

```powershell
Copy-Item .\src\RaceDay.API\RaceDay.db "$env:USERPROFILE\RaceDay-before-migrations.db"
```

Keep database backups outside the repository.

## Build and test

From the repository root:

```powershell
dotnet build .\src\RaceDay.API\RaceDay.API.csproj
dotnet build .\src\RaceDay.Web\RaceDay.Web.csproj
dotnet test .\tests\RaceDay.Tests\RaceDay.Tests.csproj
```

## Repository layout

```text
RaceDay/
|-- docs/                 Planning and project documentation
|-- src/
|   |-- RaceDay.API/      REST API, SQLite database, and migrations
|   `-- RaceDay.Web/      ASP.NET Core MVC application
|-- tests/
|   `-- RaceDay.Tests/    Automated tests
|-- .github/workflows/    GitHub Actions CI
`-- README.md
```

## CI

GitHub Actions runs repository validation when changes are pushed or a pull request is opened or updated.
