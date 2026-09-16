# Study guide: JWT authentication and policies

One of the four viva-critical pieces. §17.2 requires modifying a feature live at the viva with no
AI help, and §3 zeroes marks for code you cannot explain — so read this, then read the three core
files, then try the practice changes at the bottom without looking anything up.

## The three core files

| File | What it does | One-line explanation to give the examiner |
|---|---|---|
| [JwtTokenService.cs](../../backend/src/AgriGuard.Infrastructure/Identity/JwtTokenService.cs) | Builds and signs the access token | "Puts sub, role, district, email and a unique jti in a token signed with HMAC-SHA256, valid 15 minutes." |
| [AuthenticationSetup.cs](../../backend/src/AgriGuard.Api/Authentication/AuthenticationSetup.cs) | Validates incoming tokens | "Rejects any token with the wrong signature, algorithm, issuer or audience, or past its expiry — with zero clock skew." |
| [AuthorizationSetup.cs](../../backend/src/AgriGuard.Api/Authorization/AuthorizationSetup.cs) | The four role policies | "Only an agronomist can approve; only a dealer manages stock; only the admin edits rules; everyone but the dealer reaches farm data. Anything without [Authorize] still requires login." |

Supporting code: [AuthService.cs](../../backend/src/AgriGuard.Infrastructure/Identity/AuthService.cs)
(login, refresh rotation, logout), [Pbkdf2PasswordHasher.cs](../../backend/src/AgriGuard.Infrastructure/Identity/Pbkdf2PasswordHasher.cs),
[AuthController.cs](../../backend/src/AgriGuard.Api/Controllers/AuthController.cs).

## The request lifecycle, in order

1. `POST /api/auth/login` → `AuthService.LoginAsync` looks the user up by lower-cased email and
   verifies the PBKDF2 hash.
2. `JwtTokenService` signs an access token; `AuthService` generates a random refresh token and stores
   only its SHA-256.
3. The client sends `Authorization: Bearer <access token>` on every request.
4. The JwtBearer handler checks signature → algorithm → issuer → audience → expiry. Any failure = 401.
5. The authorization middleware applies the endpoint's policy. Wrong role = 403.
6. After 15 minutes the client calls `POST /api/auth/refresh`; the old refresh token is revoked and
   a new pair is issued.

## Decisions (and why)

**Two token lifetimes.** The access token is short (15 min) and self-contained, so no database
lookup is needed per request. The refresh token lives 14 days but is stored server-side, which is
what makes logout and revocation real. A stolen access token cannot be revoked, only outlived — that
is why it is short.

**HMAC-SHA256, not RS256.** Symmetric: one secret signs and verifies. Correct because this one API
does both. RS256 is only needed when a third party must verify tokens without being able to mint them.

**`ClockSkew = TimeSpan.Zero`.** The default is five minutes, which would let a 15-minute token live
for 20. Issuer and validator are the same server, so there is no clock drift to tolerate.

**`ValidAlgorithms = [HS256]`.** Blocks algorithm-confusion attacks, such as a token claiming
`"alg": "none"`.

**`MapInboundClaims = false`.** Without it the handler rewrites `role` into
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`, and policies checking `"role"` silently
never match.

**Refresh tokens hashed, never stored raw.** A leaked database cannot be replayed. No salt, because
the token is already 256 random bits, and an unsalted hash is what allows lookup by hash.

**Rotation with replay detection.** Presenting an already-rotated refresh token revokes every session
for that user — a spent token in someone's hands means it was copied.

**PBKDF2, 600,000 iterations** (OWASP's 2023 floor). Slowness is the point: it throttles offline
brute-force. The iteration count and salt live inside the hash string, so the cost can be raised later.

**Login never says which half was wrong**, and an unknown email still runs a dummy hash so the timing
matches. Otherwise the endpoint enumerates accounts.

**Fallback policy.** Every endpoint requires login unless marked `[AllowAnonymous]`. Forgetting
`[Authorize]` fails closed. Side effect: anonymous requests to unknown URLs get 401, not 404, so the
route map cannot be probed.

**Policies check roles; services check rows.** A policy answers "may a Farmer call this endpoint?".
"Is this *their* plot?" needs the plot loaded, so it lives in the component-A service.

## Questions to expect

1. What stops someone editing `"role": "Farmer"` to `"FieldAgronomist"` in their token?
   *(The signature covers the payload; changing one byte invalidates it, and they don't have the key.)*
2. Why `ClockSkew = TimeSpan.Zero`? What is the default?
3. React hides the Approve button for farmers. Why isn't that security?
4. Why can't you revoke an access token? What do you do instead?
5. The district is in the token. What happens when an admin moves an agronomist to another district?
   *(The old claim stays valid until the token expires — at most 15 minutes. That bound is why the token is short.)*
6. What is the difference between 401 and 403 here, and where does each come from?

## Practice changes (do these cold, then `git checkout .`)

- Let the Co-op Administrator also approve prescriptions. Which test breaks, and why is that test right?
- Shorten the access token to 5 minutes without touching C# code.
- Add a `phone` claim to the token. Then argue why it should not be there.
- Remove `ClockSkew = TimeSpan.Zero` and run `An_expired_token_is_rejected`. Explain the result.

```bash
dotnet test backend/tests/AgriGuard.IntegrationTests --filter "FullyQualifiedName~Authorization|FullyQualifiedName~Authentication|FullyQualifiedName~TokenClaims"
```

## Demo accounts

`Seed:DemoUsers` (on in Development) seeds one account per role, password `AgriGuard!Demo1`:
`farmer@`, `agronomist@`, `dealer@`, `admin@agriguard.demo`. Never enable it on a database holding
anything real — the password is in the repo.
