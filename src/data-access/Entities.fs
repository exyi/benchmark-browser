module Entities
open System
open PublicModel.AccountManagement

[<RequireQualifiedAccessAttribute>]
type AreaCapability =
    | None
    | Read
    | ReadAndWrite

type ApiTokenCapabilities = {
    AccountDetails: AreaCapability
}
with
    static member Default = { AccountDetails = AreaCapability.None; }
    static member UserLogin = { AccountDetails = AreaCapability.ReadAndWrite }
    static member WorkerToken = { ApiTokenCapabilities.Default with AccountDetails = AreaCapability.None }

[<RequireQualifiedAccessAttribute>]

type UserIdentification =
    | Email of string
    | Id of Guid

[<CLIMutableAttribute>]
type ApiLoginToken = {
    Id: Guid
    Token: byte[]
    UserId: Guid
    Totp: TotpAuthToken option
    Capabilities: ApiTokenCapabilities
    ValidTo: DateTime
    LastRenew: DateTime
    RenewInterval: TimeSpan
}

type LoginToken = {
    PasswordAndSalt: string
    IsTmpLogin: bool
    TotpAuthToken: TotpAuthToken option
}


// [<CLIMutableAttribute>]
// type AttemptedLogins = {
//     Id: Guid
//     User: Guid
//     ApiToken: Guid option
//     TotpWindowUsed: Int64 option
// }

[<CLIMutableAttribute>]
type User = {
    Id: Guid
    Email: string
    Login: LoginToken
    KeybaseUserName: string option
    GithubUserName: string option
    Roles: string array
}
with
    member x.ToUserDetails() = { UserDetails.Id = x.Id; Email = x.Email; HasTmpPassword = x.Login.IsTmpLogin; Roles = x.Roles }
    static member Create email login = { Id = Guid(); Email = email; Login = login; KeybaseUserName = None; GithubUserName = None; Roles = [||] }

[<CLIMutableAttribute>]
type KeybaseAuthRequest = {
    Id: Guid
    DataToSign: string
    UserId: Guid
    ValidTo: DateTime
}