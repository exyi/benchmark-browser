module Home.Types
open Utils
open PublicModel.ProjectManagement

type Model = {
    Data: LoadableData<ProjectListItem array>
}
with static member LiftDataUpdate msg = UpdateMsg'.lift (fun m -> m.Data) (fun m x -> { m with Data = x }) msg
