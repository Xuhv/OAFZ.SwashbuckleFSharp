module OAFZ.SwashbuckleFSharp

open Swashbuckle.AspNetCore.SwaggerGen
open System
open System.Reflection
open System.Text.Json.Serialization
open Microsoft.OpenApi.Any
open Microsoft.OpenApi.Models
open System.ComponentModel.DataAnnotations
open System.Runtime.CompilerServices

type NullableFilter() =

    let (|OptionType|NullableType|RequiredType|) (t: Type) =
        let typeInfo = t.GetTypeInfo()

        if
            t.IsDefined(typeof<RequiredAttribute>)
            || t.IsDefined(typeof<RequiredMemberAttribute>)
        then
            RequiredType
        else if
            typeInfo.IsGenericType
            && typeInfo.GetGenericTypeDefinition() = typedefof<Option<_>>
        then
            OptionType
        elif Nullable.GetUnderlyingType(t) <> null then
            NullableType
        else
            RequiredType

    let getMemberName (m: MemberInfo) =
        let inline camelCase (s: string) =
            Char.ToLower(s.[0]).ToString() + s.[1..]

        m.IsDefined(typeof<JsonPropertyNameAttribute>, false)
        |> function
            | true -> m.GetCustomAttribute<JsonPropertyNameAttribute>().Name
            | false -> camelCase m.Name

    interface ISchemaFilter with
        member _.Apply(schema, context) =
            let t = context.Type

            match t with
            | NullableType -> schema.Nullable <- true
            | OptionType ->
                schema.Nullable <- true
                schema.Type <- schema.Properties.["value"].Type
                schema.Properties.Clear()
            | RequiredType -> schema.Nullable <- false

            t.GetMembers(BindingFlags.Public ||| BindingFlags.Instance)
            |> Array.iter (fun m ->
                match m with
                | :? PropertyInfo as p -> p.PropertyType
                | :? FieldInfo as f -> f.FieldType
                | _ -> null
                |> function
                    | null -> ()
                    | RequiredType -> schema.Required.Add(getMemberName m) |> ignore
                    | _ -> ())

type DiscriminatorFilter() =

    let (|JsonPolymorphismType|Other|) (t: Type) =
        match t.IsDefined(typeof<JsonDerivedTypeAttribute>) with
        | false -> Other
        | _ ->
            t.GetCustomAttributes<JsonDerivedTypeAttribute>()
            |> Seq.length
            |> fun x -> if x > 1 then JsonPolymorphismType else Other


    interface ISchemaFilter with
        member _.Apply(schema, context) =
            match context.Type with
            | JsonPolymorphismType ->
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

type CleanFilter() =

    let (|ObjectJsonType|OtherJsonType|) (t: string) =
        match t with
        | "object" -> ObjectJsonType
        | _ -> OtherJsonType


    interface ISchemaFilter with
        member _.Apply(schema, context) =
            match schema.Type with
            | ObjectJsonType -> ()
            | _ -> schema.Required.Clear()
