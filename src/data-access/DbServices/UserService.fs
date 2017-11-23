module DataAccess.UserService
open Marten
open System
open System.Threading.Tasks
open System.Linq
open Giraffe.Tasks
open Entities
open PublicModel.AccountManagement
open PublicModel

type TotpTokenHelper = class
    static member Generate(?size: int, ?step: int) =
        let key = OtpNet.KeyGeneration.GenerateRandomKey(20) |> OtpNet.Base32Encoding.ToString
        {
            Key = key
            Size = Option.defaultValue 6 size
            Step = Option.defaultValue 15 step
        }
    static member Validate x key =
        let totp = OtpNet.Totp(OtpNet.Base32Encoding.ToBytes x.Key, step = x.Step, totpSize = x.Size)
        let mutable windowUsed = 0L
        totp.VerifyTotp(key, &windowUsed, OtpNet.VerificationWindow.RfcSpecifiedNetworkDelay), windowUsed
end

type LoginTokenHelper = class
    static member CreatePassword(pass: string, ?genTotp: bool, ?isTmp: bool) =
        let encoder = Scrypt.ScryptEncoder()
        {
            LoginToken.PasswordAndSalt = encoder.Encode(pass)
            IsTmpLogin = Option.defaultValue false isTmp
            TotpAuthToken = match genTotp with
                            | Some true -> Some (TotpTokenHelper.Generate())
                            | _ -> None
        }
    static member ValidatePassword x pass =
        let encoder = Scrypt.ScryptEncoder()
        encoder.Compare(pass, x.PasswordAndSalt)

end

type ApiTokenHelper = class
    static member GenerateToken userId validTo renewTime =
        {
            Id = Guid.NewGuid()
            Token = OtpNet.KeyGeneration.GenerateRandomKey(40)
            UserId = userId
            Totp = None
            Capabilities = ApiTokenCapabilities.UserLogin
            ValidTo = validTo
            LastRenew = DateTime.UtcNow
            RenewInterval = renewTime
        }
end


let seedUsersIfNeeded (session:IDocumentSession) = task {
    let! anyUser = session.Query<Entities.User>().AnyAsync()
    if not anyUser then
        printfn "Adding test user to database"
        // init the DB with admin
        session.Store(
            {
                User.Id = Guid.NewGuid()
                User.Email = "exyi@exyi.cz"
                User.Login = LoginTokenHelper.CreatePassword("test-password", isTmp = true)
                KeybaseUserName = None
                GithubUserName = None
                Roles = [| "Admin"; "Worker" |]
            })
}

let liftTask fn (arg: Task<'b>) = task {
    let! a = arg
    return fn a
}

let loginUser (loginData: LoginData) (session:IDocumentSession) = task {
    let loginName = loginData.Login
    let! user = (query { for u in session.Query<User>() do
                         where (u.Email = loginName)
                         take 1}).ToListAsync() |> liftTask (Seq.tryHead)
    match user with
    | None -> return LoginResult.UserDoesNotExist
    | Some user ->
        if LoginTokenHelper.ValidatePassword user.Login loginData.Password |> not then return LoginResult.WrongPassword
        else
            return match user.Login.TotpAuthToken with
                   | None -> LoginResult.Ok (user.ToUserDetails())
                   | Some totp ->
                        if String.IsNullOrWhiteSpace loginData.Otp then LoginResult.OtpRequired
                        else
                            let ok, _windowUsed = TotpTokenHelper.Validate totp loginData.Otp
                            if ok then LoginResult.Ok (user.ToUserDetails())
                            else LoginResult.WrongOtp
}

let getLoginToken (userId:Guid) (session:IDocumentSession) = task {
    let! user = session.LoadAsync<User>(userId)
    let token = ApiTokenHelper.GenerateToken user.Id (DateTime.MaxValue) (TimeSpan.FromHours(1.0))
    session.Store token
    return token
}