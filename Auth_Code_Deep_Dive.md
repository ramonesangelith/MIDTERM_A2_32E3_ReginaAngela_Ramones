# ASP.NET Core Authentication: Deep Dive & Code Dissection

This document provides a line-by-line technical breakdown of the authentication projects. It answers "Why did we write this?", "What does it do?", and "Is it good for production?".

---

## 🏗️ Universal Concepts (Used in All Levels)

### 1. `db.Database.EnsureCreated()`
*   **What it does:** It checks if the `auth.db` (SQLite file) exists. If not, it creates it **and** builds the table schema defined in your `DbSet<User>`.
*   **Why use it here?** It is perfect for **Demos and Prototypes**. You don't need to run complex Migration commands (`dotnet ef migrations add`). You just run the app, and the database appears.
*   **Why NOT use it in Production?** It cannot handle *schema changes* (e.g., adding a column). In production, you use **Migrations**.

### 2. Database vs. In-Memory List
*   **In-Memory (`List<User>`):**
    *   *Pros:* Zero setup, fast.
    *   *Cons:* **Data is lost** every time you stop the server. You can't test persistence (e.g., "Did my registration work?").
*   **Database (SQLite):**
    *   *Pros:* **Persistence**. Users stay created even if you restart the PC. Mirrors real-world architecture.
    *   *Cons:* Requires EF Core setup (lines of code).

---

## 🔒 Level 1: Basic Authentication

### Pros & Cons
*   **✅ Pro:** Extremely simple. Supported by almost every system (browsers, scripts, IoT devices).
*   **❌ Con:** **Insecure**. Credentials are sent with *every* request. If you miss HTTPS once, they are stolen. No standard "Logout".

### Key Code Explained

#### `Authorization: Basic YWRtaW46MTIz`
*   **Basic:** The scheme name. Tells the server "I am using Basic Auth".
*   **YWRtaW46MTIz:** This is `admin:123` encoded in Base64.
    *   *Note:* It is **Encoding**, NOT Encryption. Anyone can decode it.

#### `Convert.FromBase64String(header)`
*   **Reason:** The server needs the raw text "admin" and "123" to query the database. This line reverses the browser's encoding.

---

## 🍪 Level 2: Cookie Authentication

### Pros & Cons
*   **✅ Pro:** **User Experience**. The Browser handles everything. You login once, and it "just works" for days.
*   **✅ Pro:** **Sliding Expiration**. The session stays alive as long as you are active.
*   **❌ Con:** **CSRF Attacks**. Requires extra protection (Anti-Forgery Tokens) because browsers are *too* helpful (they send cookies to the wrong sites if tricked).
*   **❌ Con:** **Mobile Apps**. Non-browsers hate cookies. It's hard to manage manually.

### Key Code Explained

#### `builder.Services.AddAuthentication(...)`
*   **Definition:** This registers the core Authentication Service in the Dependency Injection container. It tells ASP.NET: "I want to support Login."

#### `.AddCookie("MyCookieAuth", options => ...)`
*   **What it does:** Configures the **Cookie Handler**.
*   **The Logic:** It enables the system to:
    1.  Serialize a User Object (ClaimsPrincipal) into bytes.
    2.  **Encrypt** those bytes using the server's private key (Data Protection).
    3.  Wrap it in a cookie named `UserSession`.
    4.  Decrypt incoming cookies back into a `User` object.

#### `HttpContext.SignInAsync(...)`
*   **The Action:** This is the Command. It says: "Take this User (Admin), Encrypt them, and send the `Set-Cookie` header to the browser."

---

## 🛂 Level 3: JWT (JSON Web Tokens)

### Pros & Cons
*   **✅ Pro:** **Stateless**. The server doesn't need to remember the user. Great for load balancing (scaling to 100 servers).
*   **✅ Pro:** **Smart Clients**. Mobile Apps (iOS/Android) and SPAs (React/Angular) prefer tokens because they have full control over storage.
*   **❌ Con:** **No Revocation**. If a token is stolen, you cannot delete it. It works until it expires.
*   **❌ Con:** **Size**. Tokens can get huge if you put too much info in them.

### Key Code Explained

#### `new JwtSecurityToken(...)`
This constructs the token. Key parts:
*   **Issuer (iss):** "Who created this?" (e.g., `MyAuthServer`).
*   **Audience (aud):** "Who is this for?" (e.g., `MyiPhoneApp`).
*   **Claims:** The user data (ID, Name, Role).
*   **Signature:** The most important part.

#### Specifying the Secret Key (`SymmetricSecurityKey`)
*   **Why:** This key is used to **Sign** the token (HMAC-SHA256).
*   **Security:** If the client changes *one single bit* of the payload (e.g., changing "User" to "Admin"), the math of the signature changes. The server will re-calculate the math, see it doesn't match, and reject the token.

#### `builder.Services.AddJwtBearer(...)`
*   **The Validation Logic.** It tells ASP.NET: "When a request comes in with `Authorization: Bearer <token>`, use **THIS** specific Secret Key to check if the signature is valid."

---

## 👮 Level 4: RBAC (Role-Based Access Control)

### Pros & Cons
*   **✅ Pro:** **Granular Control**. You block regular users from nuking your database.
*   **✅ Pro:** **Declarative**. You just decorate the function with `[Authorize]`. Clean code.
*   **❌ Con:** **Role Management**. You now need UI to assign/remove roles from users.
*   **❌ Con:** **Static**. Changes to roles (e.g., promoting a user) basically require them to log out and log back in to get a new Token/Cookie with the new claim.

### Key Code Explained

#### `[Authorize(Roles = "Admin")]`
*   **How it works:**
    1.  Authentication runs first. It creates the `User` object.
    2.  This attribute looks at `User.Claims`.
    3.  It searches specifically for a claim of type `http://schemas.../role`.
    4.  It checks if the value is "Admin".
    5.  **If Match:** Run the function.
    6.  **If Fail:** Return `403 Forbidden`.

#### `Convert.ToBase64String` (in Middleware) vs Encryption
*   *Correction/Clarification:* In Level 4, we typically use JWTs. The "Base64" discussion usually applies to the *Payload* of a JWT.
*   **JWT Payload:** Is Base64Url Encoded. It is **visible to everyone**.
*   **Security Lesson:** Never put `Password` or `SSN` in a JWT. Only put "Public" IDs.

---

## 🆔 Level 5: IdentityServer (OAuth 2.0 / OpenID Connect)

### Pros & Cons
*   **✅ Pro:** **Centralization**. One login for 50 different apps (SSO).
*   **✅ Pro:** **Standardization**. Uses strict international standards (OAuth2).
*   **❌ Con:** **Complexity**. It is a massive beast to tame.
*   **❌ Con:** **Network Chatter**. requires extra HTTP calls to fetch keys/tokens.

### Key Code Explained

#### `options.Authority = "https://..."`
*   **The Magic:** Instead of hardcoding a Secret Key (like Level 3), code tells the API: "Go to this URL. Download the **Public Key** from their `/.well-known/openid-configuration` endpoint."
*   **Why:** This allows the Key to change (rotate) securely without re-deploying your API.

#### `Grant Types` (Client Credentials)
*   **Context:** Used in the demo.
*   **Meaning:** "I am a Robot/Service." No human login involved.
*   **Use Case:** A Backend Cron Job talking to an API.

#### `Scopes` ("myApi")
*   **Meaning:** "Permission Bubbles." Even if you have a valid Badge (Token), does your badge allow you to enter the "myApi" room? 
*   **Code:** `policy.RequireClaim("scope", "myApi")` enforces this boundary.
