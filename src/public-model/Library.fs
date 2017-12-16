
module PublicModel.AccountManagement
open System

// type UserCapabilities = {
//     UserManagement: bool
//     SubmitResults: bool
// }
// with
//     static member Or(a, b) = {
//         UserManagement = a.UserManagement || b.UserManagement
//         SubmitResults = a.SubmitResults || b.SubmitResults }
//     member x.ToRoles () =
//         if x.UserManagement then ["Admin"]
//         else if x.SubmitResults then ["Worker"]
//         else []

// let UserRoles =
//     Map.ofList [
//         "Admin", { UserCapabilities.UserManagement = true; SubmitResults = true }
//         "Worker", { UserCapabilities.UserManagement = false; SubmitResults = true }
//     ]

type LoginData = {
    Login: string
    Password: string
    Otp: string
    TokenValidity: float
}

type UserDetails = {
    Id: Guid
    Email: string
    HasTmpPassword: bool
    Roles: string array
}

[<RequireQualifiedAccessAttribute>]
type LoginResult =
    | Ok of UserDetails
    | UserDoesNotExist
    | WrongPassword
    | OtpRequired
    | WrongOtp


type RegistrationData = {
    Email: string
    Password: string
    WantOtp: bool
    KeybaseClaim: string option
}

type TotpAuthToken = {
    Key: string
    Size: int
    Step: int
}

[<RequireQualifiedAccessAttribute>]
type RegistrationResult =
    | JustFailed of string
    | EmailTaken
    | Ok of UserDetails * (TotpAuthToken option)

type AuthApi = {
    login: LoginData -> UserDetails * (string option)
}

type ChangePasswordRequest = {
    OldPassword: string
    NewPassword: string
    Otp: string
}

type ChangePasswordResponse = {
    Otp: TotpAuthToken
}

type UpsertUserRequest = {
    Email: string
    Roles: string
}

[<RequireQualifiedAccessAttribute>]
type UpsertUserResponse =
    | UserCreated of password: string
    | UserUpdated