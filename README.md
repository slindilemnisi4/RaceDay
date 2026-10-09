# RaceDay

RaceDay is a full-stack web-based event management system for the South African road running, walking and cycling community.

## Project Overview

Many road events still rely on paper-based registration, spreadsheets and disconnected communication. This creates difficulties for both organisers and participants when managing event information, registrations and results.

RaceDay provides a centralised platform for managing events, categories, enrolments and results.

## User Roles

### Organiser

- Manage events
- Manage categories
- View event enrolments
- Capture participant results

### Participant

- Register and log in
- Browse events
- Enter events
- Select categories
- View own enrolments
- View personal results

## Part 1 - System Planning and Database

Part 1 established the system's foundation through:

- ERD and database design
- RESTful API planning
- SQL Server database schema and seed data
- Role-based system planning

The six approved database entities are Users, Events, Categories, Routes, Entries and Results.

## Repository Structure

```text
RaceDay/
|-- docs/
|   |-- RaceDay_ERD.png
|   `-- RaceDay_API_Endpoint_Plan.md
|-- src/
|   |-- RaceDay.API/
|   `-- RaceDay.Web/
|-- tests/
|   `-- RaceDay.Tests/
|-- RaceDay_Database.sql
|-- .github/workflows/
|   `-- raceday-ci.yml
|-- README.md
`-- .gitignore
```

The `docs` directory contains the approved Part 1 planning documentation. Part 2's API is under `src/RaceDay.API`, and Part 3's MVC foundation is under `src/RaceDay.Web`. The Part 1 database script is at the repository root.

## Technology

- SQL Server
- SQL Server Management Studio (SSMS)
- GitHub
- GitHub Actions

## Getting Started

### Prerequisites

- Microsoft SQL Server (2019 or later recommended)
- SQL Server Management Studio (SSMS)
- Git

### Setup Instructions

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Slindile48th/RaceDay.git
   cd RaceDay
   ```

2. **Open the database script:**
   - Open SQL Server Management Studio
   - Open the file `src/RaceDay_Database.sql`

3. **Execute the script:**
   - Click the **Execute** button or press `Ctrl+E`
   - The script will:
     - Create the RaceDay database
     - Create all six required tables (Users, Events, Categories, Routes, Entries, Results)
     - Apply all keys and constraints
     - Insert sample data
     - Run verification queries

4. **Verify the setup:**
   - In the Object Explorer, expand **Databases** and verify that **RaceDay** appears
   - Expand the RaceDay database and verify that all six tables are present
   - The script output will display the sample data and verification query results

### Database Structure

The RaceDay database includes:

- **Users** – system accounts for Organisers and Participants
- **Events** – races managed by Organisers
- **Categories** – event categories (e.g., age groups, distances)
- **Routes** – route information for each event
- **Entries** – participant enrolments in event categories
- **Results** – participant results and finishing positions

## CI/CD

GitHub Actions automatically validates the required RaceDay repository structure whenever changes are pushed or a pull request is opened or updated.

The successful GitHub Actions build screenshot will be added here after the workflow runs successfully:

![GitHub Actions CI Build](docs/github-actions-success.png)

## Development Status

Part 1 - System Planning and Database has been completed and verified.

Part 2 implements the RaceDay REST API. Part 3 Objective 1 establishes the ASP.NET Core MVC application foundation.

### Run the API and MVC application locally

The API and MVC app are separate projects. Start the API in one terminal:

```powershell
dotnet run --project .\src\RaceDay.API\RaceDay.API.csproj --launch-profile http
```

Then start the MVC app in another terminal:

```powershell
dotnet run --project .\src\RaceDay.Web\RaceDay.Web.csproj --launch-profile http
```

The API listens at `http://localhost:5250` and the MVC app at `http://localhost:5265`. The MVC app reads the development API address from `src/RaceDay.Web/appsettings.Development.json` (`Api:BaseUrl`). Set the `Api__BaseUrl` environment variable to an absolute HTTP or HTTPS URL when using a different API address or environment. The initial landing page does not call the API; API client methods are in place for later objectives.
