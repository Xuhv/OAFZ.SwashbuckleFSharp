module OAFZ.SwashbuckleFSharp

open Swashbuckle.AspNetCore.SwaggerGen
open System
open System.Reflection
open System.Text.Json.Serialization
open Microsoft.Extensions.DependencyInjection
open Microsoft.OpenApi.Any
open Microsoft.OpenApi.Models
open Microsoft.FSharp.Reflection

/// <summary>
/// By default, the query paramters would not auto convert to camelCase, so we need this.
/// </summary>
type CamelCaseOperationFilter() =
    interface IOperationFilter with
        member this.Apply(operation, context) =
            for p in operation.Parameters do
                p.In
                |> Option.ofNullable
                |> function
                    | Some position ->
                        if position = ParameterLocation.Path || position = ParameterLocation.Query then
                            p.Name <- Char.ToLower(p.Name[0]).ToString() + p.Name[1 .. p.Name.Length - 1]
                    | None -> ()

/// <summary>
/// Sometimes, some fields which don't exsited in client would be add to required, so delete those.
/// </summary>
type CleanExtraRequiredFieldSchemaFilter() =
    interface ISchemaFilter with
        member _.Apply(schema, context) =
            match schema.Type with
            | "object" -> ()
            | _ -> schema.Required.Clear()

/// <summary>
/// Make all field except those marked nullable required and remove all Option type.
/// </summary>
type HandleNullableMarkDocumentFilter() =
    interface IDocumentFilter with
        member _.Apply(document, context) =
            for schema in document.Components.Schemas.Values do
                for k' in schema.Properties.Keys do
                    let prop = schema.Properties[k']

                    if prop.Reference <> null then
                        let ref' = prop.Reference.ReferenceV3.Split("/") |> Array.last

                        if ref'.EndsWith("FSharpOption") then
                            prop.Type <- document.Components.Schemas[ref'].Properties["value"].Type
                            prop.Nullable <- true
                            prop.Reference <- null

                    if not prop.Nullable then
                        schema.Required.Add(k') |> ignore
                    else
                        schema.Required.Remove(k') |> ignore

            for k in document.Components.Schemas.Keys do
                if k.EndsWith("FSharpOption") then
                    document.Components.Schemas.Remove(k) |> ignore


/// <summary>
/// Mark all Nullable and Option type as nullable.
/// </summary>
type MarkNullableFieldSchemaFilter() =
    interface ISchemaFilter with
        member this.Apply(schema, context) =
            let typeInfo = context.Type.GetTypeInfo()

            match typeInfo.IsGenericType with
            | true ->
                match
                    typeInfo.GetGenericTypeDefinition() = typedefof<Nullable<_>>
                    || typeInfo.GetGenericTypeDefinition() = typedefof<Option<_>>
                with
                | true -> schema.Nullable <- true
                | false -> schema.Nullable <- false
            | false -> schema.Nullable <- false

/// <summary>
/// We can use <see cref="Swashbuckle.AspNetCore.Annotations.SwaggerDiscriminatorAttribute"></see>, but <see cref="JsonDerivedTypeAttribute" /> has provided enough metadata.
/// </summary>
type SystemTextJsonPolymorphicSchemaFilter() =
    interface ISchemaFilter with
        member _.Apply(schema, context) =
            let inline isJsonPolymorphismType (t: Type) =
                match t.IsDefined(typeof<JsonDerivedTypeAttribute>) with
                | false -> false
                | _ ->
                    t.GetCustomAttributes<JsonDerivedTypeAttribute>()
                    |> Seq.length
                    |> fun x -> x > 1

            match isJsonPolymorphismType context.Type with
            | true ->
                schema.Properties.Clear()

                context.Type.GetCustomAttributes<JsonDerivedTypeAttribute>()
                |> Seq.map (fun it -> it.DerivedType, it.TypeDiscriminator)
                |> Seq.iter (fun (ty, discriminator) ->
                    let s = context.SchemaGenerator.GenerateSchema(ty, context.SchemaRepository)
                    s.Required.Add("$type") |> ignore
                    let d = new OpenApiString(discriminator.ToString())
                    s.Properties.Add("$type", new OpenApiSchema(Default = d, Enum = [| d |], Type = "string"))
                    schema.AnyOf.Add(s))
            | _ -> ()


/// <summary>a discriminator ``$type`` is required</summary>
type UnionSchemaFilter() =
    interface ISchemaFilter with
        member this.Apply(schema, context) =
            let isOk (ty: Type) =
                match ty.IsGenericType, FSharpType.IsUnion ty with
                | false, true ->
                    FSharpType.GetUnionCases context.Type
                    |> Array.map _.GetFields()
                    |> Array.map _.Length
                    |> Array.map _.Equals(1)
                    |> Array.reduce (&&)
                | _ -> false


            match isOk context.Type with
            | false -> ()
            | _ ->
                let cases = FSharpType.GetUnionCases context.Type

                for case in cases do
                    let ty = case.GetFields() |> Array.exactlyOne |> _.PropertyType
                    let s = context.SchemaGenerator.GenerateSchema(ty, context.SchemaRepository)
                    schema.Properties.Clear()
                    schema.AnyOf.Add(s)

            for k in schema.Properties.Keys do
                if k = "$type" then
                    schema.Properties[k].Enum <- [| new OpenApiString(context.Type.Name) |]


type SwaggerGenOptions with

    /// <summary>
    /// make Swashbuckle F# friendly
    /// </summary>
    member this.UseFSharp() =
        this.SchemaFilter<SystemTextJsonPolymorphicSchemaFilter>()
        this.SchemaFilter<UnionSchemaFilter>()
        this.SchemaFilter<CleanExtraRequiredFieldSchemaFilter>()
        this.SchemaFilter<MarkNullableFieldSchemaFilter>()
        this.OperationFilter<CamelCaseOperationFilter>()
        this.DocumentFilter<HandleNullableMarkDocumentFilter>()
