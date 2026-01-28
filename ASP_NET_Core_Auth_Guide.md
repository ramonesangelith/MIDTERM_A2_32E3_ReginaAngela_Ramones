# ASP.NET Core Authentication Masterclass
**From Zero to Hero: Handling Security in Web APIs**

This guide provides a progressive learning path for implementing Authentication in ASP.NET Core Web APIs using EF Core and SQLite.

## Part 0: The Setup (Prerequisites)
All examples assume this common setup.

**1. NuGet Packages:**
*   `Microsoft.EntityFrameworkCore.Sqlite`
*   `Microsoft.AspNetCore.Authentication.JwtBearer`

**2. The User Entity:**
```csharp
public class User {
    public int Id { get; set; }
    public string Username { get; set; }
    public string Password { get; set; } // HASH in production!
    public string Role { get; set; } // "Admin" or "User"
}
```

---

## Part 1: Level 1 - Basic Authentication

### 1. Theory
**HTTP Basic Authentication** is defined in RFC 7617. It is a method for an HTTP user agent (e.g., a web browser) to provide a user name and password when making a request.

### 2. Concept
Think of this like a secret handshake required at the door *every single time* you enter the room. The layout of the "handshake" is simply `username:password` glued together.

### 3. Discussion
The credentials are encoded in **Base64** and placed in the `Authorization` header.
*   Format: `Authorization: Basic <base64-string>`
Since the server does not remember the user, the client must resend this header for every API call.

### 4. Disadvantages
*   **Security Risk:** Base64 is NOT encryption. It is readable by anyone who intercepts the traffic. HTTPS is mandatory.
*   **Performance:** The database is queried to verify credentials on every single request, which adds latency.
*   **No "Log Out":** Since there is no session, you cannot "kill" a login server-side.

### 5. Example Code (Middleware)
```csharp
// BasicAuthMiddleware.cs
public async Task Invoke(HttpContext context, AppDbContext db) {
    if (!context.Request.Headers.ContainsKey("Authorization")) {
        context.Response.StatusCode = 401; 
        return;
    }

    try {
        var header = AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]);
        var bytes = Convert.FromBase64String(header.Parameter);
        var credentials = Encoding.UTF8.GetString(bytes).Split(':');
        
        var user = await db.Users.FirstAsync(u => u.Username == credentials[0] && u.Password == credentials[1]);
        context.Items["User"] = user; // Authentication Successful
    } catch {
        // Malformed header or User not found
        context.Response.StatusCode = 401;
        return;
    }
    await _next(context);
}
```

### 6. Example Testing (Postman)
1.  **Method:** `GET`
2.  **URL:** `https://localhost:7000/api/secure-data`
3.  **Tab:** Select **Authorization**.
4.  **Type:** Select **Basic Auth**.
5.  **Username:** `admin` | **Password:** `123`
6.  **Send:** Expect `200 OK`.
7.  **Test Failure:** Change password to `wrong`. Send. Expect `401 Unauthorized`.

---

## Part 2: Level 2 - Cookie Authentication

### 1. Theory
**Stateful Authentication** relies on the server creating a record of the authenticated user in its memory or database and issuing a reference ID to the client.

### 2. Concept
Think of a Coat Check ticket. You give your coat (Credentials), and they give you a ticket number (Cookie). You don't carry the coat around; you just show the ticket to prove ownership.

### 3. Discussion
When a user logs in, ASP.NET Core Encrypts the user Identity into a string and sets it as a `Set-Cookie` HTTP header. The Browser automatically stores this and sends it back on every request to that domain.

### 4. Disadvantages
*   **CSRF (Cross-Site Request Forgery):** Because browsers send cookies automatically, malicious sites can trigger actions on your behalf without you knowing.
*   **Mobile Limitations:** Non-browser clients (iOS apps, Unity games) do not manage cookies automatically like Chrome/Edge does.

### 5. Example Code (Controller)
```csharp
[HttpPost("login")]
public async Task<IActionResult> Login(string username, string password) {
    var user = _db.Users.SingleOrDefault(x => x.Username == username && x.Password == password);
    if (user == null) return Unauthorized();

    var claims = new List<Claim> { new Claim(ClaimTypes.Name, user.Username) };
    var identity = new ClaimsIdentity(claims, "MyCookieAuth");
    
    // This creates the encrypted cookie
    await HttpContext.SignInAsync("MyCookieAuth", new ClaimsPrincipal(identity));
    return Ok("Logged In");
}
```

### 6. Example Testing (Postman)
1.  **Method:** `POST`
2.  **URL:** `https://localhost:7000/api/auth/login`
3.  **Params:** `?username=admin&password=123`
4.  **Send:** Expect `200 OK`.
5.  **Check Cookies:** Click the **Cookies** link under the Send button. Verify `UserSession` (or default cookie name) is present.
6.  **Next Request:** Create a new `GET` request to a secure endpoint. Leave Auth as "No Auth". Postman will auto-attach the cookie. Send. Expect `200 OK`.

---

## Part 3: Level 3 - JWT (JSON Web Token)

### 1. Theory
**Stateless Authentication** using RFC 7519. It defines a compact and self-contained way for securely transmitting information between parties as a JSON object.

### 2. Concept
Think of a **Passport**. It contains your photo, name, and an official holographic seal (Signature). You carry it with you. The authorities don't need to call your home country to verify it; they just check the seal.

### 3. Discussion
A JWT has 3 parts: Header, Payload (Claims), and Signature.
The server signs the token using a **Secret Key**. If a user tries to change "Role: User" to "Role: Admin", the signature calculation will fail, and the server will reject it.

### 4. Disadvantages
*   **Revocation Difficulty:** Since the server doesn't store the token, it cannot easily "invalidate" it before it expires (unlike deleting a session).
*   **Bandwidth:** A token with many claims can be large, and it is sent with every single request.

### 5. Example Code (Generation)
```csharp
[HttpPost("login")]
public IActionResult Login(LoginDto request) {
    // 1. Check User
    var user = _db.Users.SingleOrDefault(u => u.Username == request.Username && u.Password == request.Password);
    if (user == null) return Unauthorized();

    // 2. Build Claims
    var claims = new List<Claim> { 
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role) 
    };

    // 3. Sign Token
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("YourSuperSecretKey123!"));
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.Now.AddHours(1),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    );

    return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
}
```

### 6. Example Testing (Postman)
1.  **Login:** `POST` to `/login`. Copy the `token` string from the JSON response.
2.  **Access:** Create a `GET` request to `/secure`.
3.  **Tab:** **Authorization**.
4.  **Type:** **Bearer Token**.
5.  **Token:** Paste the string.
6.  **Send:** Expect `200 OK`.
7.  **Test Tampering:** Change the last letter of the token in Postman. Send. Expect `401 Unauthorized` (Signature Fail).

---

## Part 4: Level 4 - RBAC (Role-Based Access Control)

### 1. Theory
**Authorization** mechanism that restricts system access to authorized users based on their roles.

### 2. Concept
Think of **Security Clearance Levels** (Confidential, Secret, Top Secret). Just because you can enter the building (Authentication) doesn't mean you can enter the High Security Vault (Authorization).

### 3. Discussion
In ASP.NET Core, this is handled by the `[Authorize(Roles = "...")]` attribute. The framework looks for `ClaimTypes.Role` inside the authenticated User Principal (whether from Cookie or JWT).

### 4. Disadvantages
*   **Hardcoded Strings:** Using `[Authorize(Roles = "Admin")]` spreads magic strings across controllers.
*   **Hierarchy Complexity:** Handling "Managers can do everything Employees can do" requires custom policy logic or multiple roles.

### 5. Example Code
```csharp
[Authorize(Roles = "Admin")] // Use Comma for OR logic: "Admin,Manager"
[HttpDelete("delete-all")]
public IActionResult DeleteAllData() {
    return Ok("System Wiped");
}
```

### 6. Example Testing (Postman)
1.  **Scenario:** User A is "Admin", User B is "User".
2.  **Login as User B:** Get Token.
3.  **Attempt:** `DELETE /delete-all` with User B's token.
4.  **Result:** `403 Forbidden` (The Server knows who you are, but refuses access).
5.  **Login as User A:** Get Token.
6.  **Attempt:** `DELETE /delete-all` with User A's token.
7.  **Result:** `200 OK`.

---

## Part 5: Level 5 - IdentityServer (OAuth 2.0 & OpenID Connect)

### 1. Theory
**IdentityServer** (using Duende IdentityServer for .NET 6+) implements the **OpenID Connect (OIDC)** and **OAuth 2.0** protocols. It acts as a standalone "Security Token Service" (STS).

### 2. Concept
Think of the **DMV (Department of Motor Vehicles)**. 
*   The **API** (Bar/Club) doesn't issue IDs; it just checks them.
*   The **IdentityServer** (DMV) verifies who you are and issues the ID (Token).
*   **SSO (Single Sign-On):** Once you have the ID, you can enter the Bar, the Bank, and the Library without registering at each one separately.

### 3. Discussion
In this architecture, your API **removes all login logic**. It simply trusts the IdentityServer.
1.  Client asks IdentityServer for a Token.
2.  IdentityServer verifies credentials (or Google/Facebook login) and signs a JWT.
3.  Client sends JWT to API.
4.  API verifies the signature against the IdentityServer's public key (retrieved automatically via Metadata).

### 4. Disadvantages
*   **Extreme Complexity:** Setting up an OIDC Provider is significantly harder than a simple JWT loop.
*   **Cost:** "Duende IdentityServer" requires a paid license for commercial use (revenue > $1M).
*   **Overkill:** Generally too heavy for a simple, single-tenant application.

### 5. Example Code
**A. The Identity Server (Config.cs - Defining Resources):**
```csharp
public static IEnumerable<ApiScope> ApiScopes =>
    new List<ApiScope> { new ApiScope("myApi", "My Custom API") };

public static IEnumerable<Client> Clients =>
    new List<Client> {
        new Client {
            ClientId = "postman_client",
            AllowedGrantTypes = GrantTypes.ClientCredentials, // Machine-to-Machine
            ClientSecrets = { new Secret("super_secret".Sha256()) },
            AllowedScopes = { "myApi" }
        }
    };
```

**B. The API (Program.cs - Consuming the Token):**
Notice we mostly just specify the "Authority" URL. The API downloads the public keys automatically!

```csharp
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = "https://localhost:5001"; // The IdP URL
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false // Simplified for demo
        };
    });

builder.Services.AddAuthorization(options => {
    options.AddPolicy("ApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "myApi");
    });
});
```

### 6. Example Testing (Postman)
1.  **Scenario:** Get a token from the IdP using Client Credentials (Machine-to-Machine).
2.  **Request:** `GET https://localhost:7000/api/secure` (The API).
3.  **Tab:** **Authorization**.
4.  **Type:** **OAuth 2.0**.
5.  **Configure New Token:**
    *   **Grant Type:** Client Credentials
    *   **Access Token URL:** `https://localhost:5001/connect/token` (The IdP's Token Endpoint)
    *   **Client ID:** `postman_client`
    *   **Client Secret:** `super_secret`
    *   **Scope:** `myApi`
6.  **Action:** Click **Get New Access Token**.
    *   *Postman will call the IdP, get the JWT, and show it to you.*
7.  **Action:** Click **Use Token**.
8.  **Send:** Expect `200 OK` from the API.

