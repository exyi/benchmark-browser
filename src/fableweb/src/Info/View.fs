module Info.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

let root =
  div
    [ ClassName "content" ]
    [ h1
        [ ]
        [ str "About page" ]
      p
        [ ]
        [ str "See github "
          a [ Href "https://github.com/exyi/benchmark-browser" ] [ str "exyi/benchmark-browser" ]
         ] ]
