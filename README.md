# OAFZ.SwashbuckleFSharp

```fsharp
builder.Services.AddEndpointsApiExplorer().AddSwaggerGen(fun c -> c.UseFSharp())
```

### DU

```fsharp
type A = 
    { FieldA: string }

    member _.``$type`` = nameof A

type B = 
    { FieldB: string }

    member _.``$type`` = nameof B

[JsonConverter(typeof<YourConverter>)]
type AB = 
    | A of A
    | B of B
```

There should be only one field in each union case.
