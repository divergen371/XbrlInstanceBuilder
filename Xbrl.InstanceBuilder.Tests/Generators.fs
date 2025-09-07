module Generators

open FsCheck
open System
open System.Xml.Linq
open Xbrl.InstanceBuilder

// Simple NCName generator (letters only)
let private letters = [ 'a' .. 'z' ] @ [ 'A' .. 'Z' ]

let ncNameGen =
    FsCheck.Gen.choose(1, 8)
    |> FsCheck.Gen.bind (fun len ->
        FsCheck.Gen.arrayOfLength len (FsCheck.Gen.elements letters)
        |> FsCheck.Gen.map (fun chars -> String(chars)))

// Arbitrary providers for property attributes
// Usage: [<Property(Arbitrary=[| typeof<Generators.DomainArb> |])>]
//        let ``property using NcName`` (prefix: string) = ...

type DomainArb =
    static member NcName() : Arbitrary<string> = FsCheck.Arb.fromGen ncNameGen
    static member LocalName() : Arbitrary<string> = FsCheck.Arb.fromGen ncNameGen
    static member XNameJppfs() : Arbitrary<XName> =
        FsCheck.Arb.fromGen (ncNameGen |> FsCheck.Gen.map (fun ln -> Ns.jppfs + ln))
    static member AxisMemberJppfs() : Arbitrary<AxisMember> =
        let genAxisMember =
            FsCheck.Gen.map2 (fun a m -> { Axis = Ns.jppfs + a; Member = Ns.jppfs + m }) ncNameGen ncNameGen
        FsCheck.Arb.fromGen genAxisMember
