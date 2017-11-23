module DataClasses


type WorkerConfig = {
    ApiEndpoint: string
    ApiUser: string
    ApiPassword: string
    ApiOtp: PublicModel.AccountManagement.TotpAuthToken option
    ClonedRepositories: string
}