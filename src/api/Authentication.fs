module Authentication
open DataAccess
open PublicModel.AccountManagement
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Http
open Giraffe.Tasks
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open System.IdentityModel.Tokens.Jwt
open System.Security.Claims
open System
open System.IdentityModel.Tokens.Jwt


let getCurrentUserId (ctx: HttpContext) =
    (ctx.User.Claims |> Seq.tryFind (fun c -> c.Type = JwtRegisteredClaimNames.UniqueName || c.Type = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")) |> Option.map (fun x -> Guid.Parse x.Value) |> Option.defaultValue Guid.Empty

type SecretKey = { Key: SecurityKey }
let usedSigningAlgo = SecurityAlgorithms.HmacSha256
let createJwt key claims =
    let cred = SigningCredentials(key, usedSigningAlgo)
    let header = JwtHeader(cred)
    let payload = JwtPayload(claims)
    JwtSecurityToken(header, payload)

let login (ctx:HttpContext) (data:LoginData) =
    let getClaims (userinfo:UserDetails) =
        [
            [ Claim(JwtRegisteredClaimNames.Email, userinfo.Email)
              Claim(JwtRegisteredClaimNames.Nbf, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
              Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddDays(data.TokenValidity).ToUnixTimeSeconds().ToString()) ]

            (userinfo.Roles |> Seq.map (fun f -> Claim(ClaimTypes.Role, f)) |> Seq.toList)

            [ Claim(JwtRegisteredClaimNames.UniqueName, userinfo.Id.ToString()) ]

            (if userinfo.HasTmpPassword then [] else [ Claim(ClaimTypes.Role, "Valid") ])

        ] |> List.collect id
    DatabaseOperation.execOperation ctx (fun s -> task {
        let! loginResult = UserService.loginUser data s
        return match loginResult with
               | LoginResult.Ok user ->
                   let claims = (getClaims user)
                   let token = createJwt (ctx.RequestServices.GetRequiredService<SecretKey>()).Key claims
                   loginResult, Some (JwtSecurityTokenHandler().WriteToken(token))
               | _ -> loginResult, None
    })