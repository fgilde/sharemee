# Sign in to Sharemee

Start the app with `dotnet run --project src/Shareme.AppHost`, open *shareme-web* from the Aspire dashboard and sign in with one of these users:

| User | Password | Roles |
|---|---|---|
| `admin@sharemee.local` | `Sm1!lmyfOhHoylC0` | Administrator |
| `user@sharemee.local` | `Sm1!06houIzPkGGB` | Member (reads, uploads and edits content, works in workspaces) |

The *Keycloak* button on the sign-in page takes the same users with the same passwords (realm `shareme`); the app host brings the realm up to date on every start.

The users and passwords come from `src/Shareme.Migrations/DemoUsers.cs` and are seeded on the first start of an empty database. A database that was set up before (for example through the setup wizard) keeps its own accounts: sign in with the administrator you created there, or delete the app host's Postgres volume to start empty. To walk through the setup wizard yourself, start with `--Shareme:DemoUsers=false` on an empty database; the setup token is in the log of *shareme-api*.

The passwords are for development only: change them before you deploy anywhere.
