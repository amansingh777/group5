# Code review checklist

Use this for every change. Each point is yes, no, or not applicable, with the file and line for every no.

## Correctness
1. Does the code do what the ticket or spec says, including the edge cases (empty list, first and last day of the month, nothing found)?
2. Are dates and times handled consistently, and are the tests reliable and repeatable?
3. Could two requests at the same moment leave the data wrong (stock going negative, a double booking, a lost update)?
4. Are percentages and ratios divided by the right thing, and is a zero divisor handled?

## Data and performance
5. Does any list endpoint run one query per row it returns?
6. Do the SQL migration, the EF model and the validators agree on column length, nullability and indexes?
7. Is any existing migration edited? (A change must be a new migration.)
8. Are queries bounded by paging or a sensible limit?

## Security and privacy
9. Is any secret, key or password committed in a config file, compose file or test?
10. Does any endpoint return more fields than the caller needs (password hashes, full personal details)?
11. Is personal data about customers or staff collected, stored or shown only where there is a lawful reason?
12. Is user input validated on the server, not only in the UI?

## Design
13. Does the change follow Controllers, then Services, then Repositories, with DTOs and mappers at the edge?
14. Are business rules in services and not in controllers or repositories?
15. Are errors returned as ProblemDetails with the right status code (400, 404, 409, 422)?

## Tests
16. Does every new rule have a test that would fail if the rule were removed?
17. Does each test assert the thing its name says, and fail for the right reason?
18. Do any tests depend on the current date or time, the order they run in, or data another test created?

## Delivery
19. Does CI run the same commands a developer runs locally, with the same .NET and Node versions and the services it needs?
20. Do the README and CLAUDE.md still describe the project as it is?
