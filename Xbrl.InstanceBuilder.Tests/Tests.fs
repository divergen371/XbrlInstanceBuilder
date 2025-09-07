module Tests

open System
open System.IO
open System.Globalization
open System.Xml.Linq
open Xunit
open FsUnit.Xunit
open FsCheck
open FsCheck.Xunit
open Xbrl.InstanceBuilder

let private getAttr (el: XElement) (name: XName) =
    el.Attribute(name) |> Option.ofObj |> Option.map (fun a -> a.Value)

let private toNcName (s: string) =
    let letters = s |> Seq.filter Char.IsLetter |> Seq.toArray
    if letters.Length = 0 then "p" else String letters

[<Fact>]
let ``schemaRef http is forced to https`` () =
    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref =
            Some
                "http://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd" }

    let doc = buildDocument args
    let root = doc.Root
    let sref = root.Element(Ns.link + "schemaRef")
    let href = getAttr sref (Ns.xlink + "href")
    href.Value |> should startWith "https://"

[<Fact>]
let ``PrefixMap controls explicitMember QName`` () =
    let am: AxisMember =
        { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
          Member = Ns.jppfs + "ConsolidatedMember" }

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))
          Dimensions = [ am ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts =
            [ { QName = Ns.jppfs + "NetSales"
                ContextRef = "C1"
                UnitRef = "U1"
                Decimals = -3
                Rounding = RoundingPolicy.Truncate
                Value = 1m } ]
          SchemaRefHref = None }

    let prefixes: PrefixMap = [ string Ns.jppfs, "pfs" ] |> Map.ofList
    let doc = buildDocumentWithNamespaces prefixes args
    let ctx = doc.Root.Element(Ns.xbrli + "context")
    let seg = ctx.Element(Ns.xbrli + "segment")
    let em = seg.Element(Ns.xbrldi + "explicitMember")
    let dim = getAttr em (XName.Get "dimension")
    dim.Value |> should equal "pfs:ConsolidatedOrNonConsolidatedAxis"
    em.Value |> should equal "pfs:ConsolidatedMember"

[<Fact>]
let ``Builder deduplicates context and unit`` () =
    let b0 = Builder.init "5493001KJTIIGC8Y1R12" None
    let period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))

    let dims =
        [ { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
            Member = Ns.jppfs + "ConsolidatedMember" } ]

    let b1, _, _ =
        Builder.addFact
            (Ns.jppfs + "NetSales")
            period
            dims
            "iso4217:JPY"
            -3
            RoundingPolicy.Truncate
            1m
            b0

    let b2, _, _ =
        Builder.addFact
            (Ns.jppfs + "OperatingIncome")
            period
            dims
            "iso4217:JPY"
            -3
            RoundingPolicy.Truncate
            2m
            b1

    let doc = Builder.build b2
    let contexts = doc.Root.Elements(Ns.xbrli + "context") |> Seq.length
    let units = doc.Root.Elements(Ns.xbrli + "unit") |> Seq.length
    contexts |> should equal 1
    units |> should equal 1

[<Fact>]
let ``buildDocumentWithConfig resolves schemaRef from json`` () =
    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let candidates =
        [ "../../../xbrl.instancebuilder.json"
          "../../../../xbrl.instancebuilder.json" ]
        |> List.map (fun rel ->
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel)))

    let cfgPath =
        candidates
        |> List.tryFind File.Exists
        |> Option.defaultValue (candidates |> List.last)

    let doc = buildDocumentWithConfig (Some cfgPath) args

    let href =
        doc.Root
            .Element(Ns.link + "schemaRef")
            .Attribute(Ns.xlink + "href")
            .Value

    href |> should haveSubstring "entryPoint_"

[<Fact>]
let ``tryBuildDocument returns errors on invalid input`` () =
    let args: BuildArgs =
        { Lei = "" // missing
          ContextId = ""
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    match tryBuildDocument args with
    | Ok _ -> failwith "expected errors"
    | Error es -> es.Length |> should be (greaterThanOrEqualTo 2)

// ----------------------------
// FsCheck property-based tests
// ----------------------------

[<Property(MaxTest = 200)>]
let ``Truncate does not overshoot sign`` (decimals: int) (v: decimal) =
    let d =
        if decimals > 6 then 6
        elif decimals < -6 then -6
        else decimals

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts =
            [ { QName = Ns.jppfs + "NetSales"
                ContextRef = "C1"
                UnitRef = "U1"
                Decimals = d
                Rounding = RoundingPolicy.Truncate
                Value = v } ]
          SchemaRefHref = None }

    let doc = buildDocument args
    let fact = doc.Root.Element(Ns.jppfs + "NetSales")
    let outv = Decimal.Parse(fact.Value, CultureInfo.InvariantCulture)
    if v >= 0m then outv <= v else outv >= v

[<Property(MaxTest = 100)>]
let ``Builder deduplicates context/unit for same key (property)``
    (values: NonEmptyArray<decimal>)
    =
    let vs = values.Get |> Array.truncate 20
    let b0 = Builder.init "5493001KJTIIGC8Y1R12" None
    let period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))

    let dims =
        [ { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
            Member = Ns.jppfs + "ConsolidatedMember" } ]

    let bF =
        vs
        |> Array.fold
            (fun b v ->
                let b', _, _ =
                    Builder.addFact
                        (Ns.jppfs + "NetSales")
                        period
                        dims
                        "iso4217:JPY"
                        -3
                        RoundingPolicy.Truncate
                        v
                        b

                b')
            b0

    let doc = Builder.build bF
    let contexts = doc.Root.Elements(Ns.xbrli + "context") |> Seq.length
    let units = doc.Root.Elements(Ns.xbrli + "unit") |> Seq.length
    contexts = 1 && units = 1

[<Property(MaxTest = 100)>]
let ``PrefixMap applies to explicitMember qname (property)``
    (rawPrefix: string)
    =
    let p = toNcName rawPrefix

    let am: AxisMember =
        { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
          Member = Ns.jppfs + "ConsolidatedMember" }

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))
          Dimensions = [ am ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let prefixes: PrefixMap = [ string Ns.jppfs, p ] |> Map.ofList
    let doc = buildDocumentWithNamespaces prefixes args

    let em =
        doc.Root
            .Element(Ns.xbrli + "context")
            .Element(Ns.xbrli + "segment")
            .Element(Ns.xbrldi + "explicitMember")

    let dim = em.Attribute(XName.Get "dimension").Value
    dim.StartsWith(p + ":") && em.Value.StartsWith(p + ":")

[<Property(MaxTest = 200)>]
let ``Truncate is between RoundDown and RoundHalfUp``
    (decimals: int)
    (v: decimal)
    =
    let d =
        if decimals > 6 then 6
        elif decimals < -6 then -6
        else decimals

    let mk policy =
        let args: BuildArgs =
            { Lei = "5493001KJTIIGC8Y1R12"
              ContextId = "C1"
              Period = Period.Instant(DateTime(2025, 3, 31))
              Dimensions = []
              UnitId = "U1"
              MeasureQName = "iso4217:JPY"
              Facts =
                [ { QName = Ns.jppfs + "NetSales"
                    ContextRef = "C1"
                    UnitRef = "U1"
                    Decimals = d
                    Rounding = policy
                    Value = v } ]
              SchemaRefHref = None }

        let doc = buildDocument args

        Decimal.Parse(
            doc.Root.Element(Ns.jppfs + "NetSales").Value,
            CultureInfo.InvariantCulture
        )

    let rd = mk RoundingPolicy.RoundDown
    let tr = mk RoundingPolicy.Truncate
    let rh = mk RoundingPolicy.RoundHalfUp
    let lo = min rd rh
    let hi = max rd rh
    if v >= 0m then lo <= tr && tr <= hi else tr >= hi

[<Property(MaxTest = 100)>]
let ``Builder order invariance for two facts (set equality)``
    (a: decimal)
    (b: decimal)
    =
    let period = Period.Duration(DateTime(2024, 4, 1), DateTime(2025, 3, 31))

    let dims =
        [ { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
            Member = Ns.jppfs + "ConsolidatedMember" } ]

    let mk f1 f2 =
        let b0 = Builder.init "5493001KJTIIGC8Y1R12" None

        let b1, _, _ =
            Builder.addFact
                (Ns.jppfs + f1)
                period
                dims
                "iso4217:JPY"
                -3
                RoundingPolicy.Truncate
                a
                b0

        let b2, _, _ =
            Builder.addFact
                (Ns.jppfs + f2)
                period
                dims
                "iso4217:JPY"
                -3
                RoundingPolicy.Truncate
                b
                b1

        Builder.build b2

    let d1 = mk "NetSales" "OperatingIncome"
    let d2 = mk "OperatingIncome" "NetSales"

    let facts (doc: XDocument) =
        [ "NetSales"; "OperatingIncome" ]
        |> List.choose (fun ln ->
            let el = doc.Root.Element(Ns.jppfs + ln)

            if isNull (box el) then
                None
            else
                Some(ln, Decimal.Parse(el.Value, CultureInfo.InvariantCulture)))
        |> List.sortBy id

    let f1 = facts d1
    let f2 = facts d2
    let c1 = d1.Root.Elements(Ns.xbrli + "context") |> Seq.length
    let c2 = d2.Root.Elements(Ns.xbrli + "context") |> Seq.length
    let u1 = d1.Root.Elements(Ns.xbrli + "unit") |> Seq.length
    let u2 = d2.Root.Elements(Ns.xbrli + "unit") |> Seq.length
    f1 = f2 && c1 = 1 && c2 = 1 && u1 = 1 && u2 = 1

[<Property(MaxTest = 200)>]
let ``RoundHalfUp error is within theoretical bound``
    (decimals: int)
    (v: decimal)
    =
    let d =
        if decimals > 6 then 6
        elif decimals < -6 then -6
        else decimals

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts =
            [ { QName = Ns.jppfs + "NetSales"
                ContextRef = "C1"
                UnitRef = "U1"
                Decimals = d
                Rounding = RoundingPolicy.RoundHalfUp
                Value = v } ]
          SchemaRefHref = None }

    let doc = buildDocument args

    let outv =
        Decimal.Parse(
            doc.Root.Element(Ns.jppfs + "NetSales").Value,
            CultureInfo.InvariantCulture
        )

    let diff = abs (outv - v)

    let pow10 n =
        if n <= 0 then
            1m
        else
            [ 1..n ] |> List.fold (fun acc _ -> acc * 10m) 1m

    let scale = pow10 (abs d)
    let bound = if d >= 0 then 0.5m / scale else 0.5m * scale
    diff <= bound

[<Fact>]
let ``buildDocument declares default namespaces on root`` () =
    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let doc = buildDocument args
    let attrs = doc.Root.Attributes() |> Seq.toList

    let ns name =
        attrs |> Seq.exists (fun a -> a.Name = (XNamespace.Xmlns + name))

    ns "xbrli" |> should be True
    ns "xbrldi" |> should be True
    ns "iso4217" |> should be True
    ns "jppfs_cor" |> should be True
    ns "jpcrp_cor" |> should be True
    ns "jpdei_cor" |> should be True
    ns "link" |> should be True
    ns "xlink" |> should be True

[<Property(MaxTest = 100)>]
let ``buildDocumentWithNamespaces declares given prefixes only (plus defaults)``
    (rawPrefix: string)
    =
    let p = toNcName rawPrefix
    let prefixes: PrefixMap = [ string Ns.jppfs, p ] |> Map.ofList

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions =
            [ { Axis = Ns.jppfs + "A"
                Member = Ns.jppfs + "B" } ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let doc = buildDocumentWithNamespaces prefixes args
    let attrs = doc.Root.Attributes() |> Seq.toList

    let hasNs name =
        attrs |> Seq.exists (fun a -> a.Name = (XNamespace.Xmlns + name))
    // always present defaults
    hasNs "xbrli" |> should be True
    hasNs "xbrldi" |> should be True
    hasNs "iso4217" |> should be True
    hasNs "link" |> should be True
    hasNs "xlink" |> should be True
    // provided prefix exists
    hasNs p |> should be True
    // taxonomy defaults are not auto-declared unless provided
    hasNs "jppfs_cor" |> should be False
    hasNs "jpcrp_cor" |> should be False
    hasNs "jpdei_cor" |> should be False

[<Fact>]
let ``qnameOfWith falls back to qnameOf when prefix missing`` () =
    let prefixes: PrefixMap = Map.empty

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions =
            [ { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
                Member = Ns.jppfs + "ConsolidatedMember" } ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let doc = buildDocumentWithNamespaces prefixes args

    let em =
        doc.Root
            .Element(Ns.xbrli + "context")
            .Element(Ns.xbrli + "segment")
            .Element(Ns.xbrldi + "explicitMember")

    let dim = em.Attribute(XName.Get "dimension").Value
    dim |> should startWith "jppfs_cor:"
    em.Value |> should startWith "jppfs_cor:"

[<Property(MaxTest = 100)>]
let ``schemaRef http is upgraded to https, https preserved`` (useHttp: bool) =
    let baseUrl =
        "disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd"

    let hrefIn = (if useHttp then "http://" else "https://") + baseUrl

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = Some hrefIn }

    let doc = buildDocument args

    let hrefOut =
        doc.Root
            .Element(Ns.link + "schemaRef")
            .Attribute(Ns.xlink + "href")
            .Value

    hrefOut.StartsWith("https://")

[<Property(MaxTest = 100)>]
let ``Validation succeeds when prefixes are declared`` (rawPrefix: string) =
    let p = toNcName rawPrefix

    let am: AxisMember =
        { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
          Member = Ns.jppfs + "ConsolidatedMember" }

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = [ am ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let prefixes: PrefixMap = [ string Ns.jppfs, p ] |> Map.ofList
    let doc = buildDocumentWithNamespaces prefixes args
    let errs = Validation.validateDocument doc
    errs
    |> List.exists (function
        | Validation.MissingNamespacePrefix _ -> true
        | _ -> false)
    |> not
    
[<Fact>]
let ``Validation detects missing context and unit`` () =
    let root = XElement(Ns.xbrli + "xbrl")
    // add required xmlns to avoid other errors
    root.Add(XAttribute(XNamespace.Xmlns + "xbrli", string Ns.xbrli))
    root.Add(XAttribute(XNamespace.Xmlns + "xbrldi", string Ns.xbrldi))
    root.Add(XAttribute(XNamespace.Xmlns + "iso4217", string Ns.iso4217))
    root.Add(XAttribute(XNamespace.Xmlns + "link", string Ns.link))
    root.Add(XAttribute(XNamespace.Xmlns + "xlink", string Ns.xlink))
    root.Add(
        XElement(
            Ns.link + "schemaRef",
            XAttribute(Ns.xlink + "type", "simple"),
            XAttribute(Ns.xlink + "href", "https://example.com/entry.xsd")
        )
    )
    let doc = XDocument(root)
    let errs = Validation.validateDocument doc
    errs |> List.exists (function Validation.MissingContext -> true | _ -> false) |> should be True
    errs |> List.exists (function Validation.MissingUnit -> true | _ -> false) |> should be True

[<Fact>]
let ``Validation detects duplicate contextId and unitId`` () =
    let doc =
        let args : BuildArgs =
            { Lei = "5493001KJTIIGC8Y1R12"
              ContextId = "C1"
              Period = Period.Instant(DateTime(2025,3,31))
              Dimensions = []
              UnitId = "U1"
              MeasureQName = "iso4217:JPY"
              Facts = []
              SchemaRefHref = None }
        let d = buildDocument args
        // duplicate context and unit by adding another with the same id
        d.Root.Add(XElement(d.Root.Element(Ns.xbrli + "context")))
        d.Root.Add(XElement(d.Root.Element(Ns.xbrli + "unit")))
        d
    let errs = Validation.validateDocument doc
    errs |> List.exists (function Validation.DuplicateContextId _ -> true | _ -> false) |> should be True
    errs |> List.exists (function Validation.DuplicateUnitId _ -> true | _ -> false) |> should be True

[<Fact>]
let ``Validation detects unknown contextRef and unitRef`` () =
    let root = XElement(Ns.xbrli + "xbrl")
    root.Add(XAttribute(XNamespace.Xmlns + "xbrli", string Ns.xbrli))
    root.Add(XAttribute(XNamespace.Xmlns + "xbrldi", string Ns.xbrldi))
    root.Add(XAttribute(XNamespace.Xmlns + "iso4217", string Ns.iso4217))
    root.Add(XAttribute(XNamespace.Xmlns + "link", string Ns.link))
    root.Add(XAttribute(XNamespace.Xmlns + "xlink", string Ns.xlink))
    root.Add(
        XElement(
            Ns.link + "schemaRef",
            XAttribute(Ns.xlink + "type", "simple"),
            XAttribute(Ns.xlink + "href", "https://example.com/entry.xsd")
        )
    )
    // add one context/unit with id that won't match the fact
    root.Add(XElement(Ns.xbrli + "context", XAttribute(XName.Get "id", "C_OK")))
    root.Add(XElement(Ns.xbrli + "unit", XAttribute(XName.Get "id", "U_OK"), XElement(Ns.xbrli + "measure", "iso4217:JPY")))
    // add a fact that points to non-existing IDs
    root.Add(
        XElement(Ns.jppfs + "NetSales",
            XAttribute(XName.Get "contextRef", "C_MISSING"),
            XAttribute(XName.Get "unitRef", "U_MISSING"),
            XAttribute(XName.Get "decimals", "0"),
            "123"))
    let doc = XDocument(root)
    let errs = Validation.validateDocument doc
    errs |> List.exists (function Validation.UnknownContextRef v when v = "C_MISSING" -> true | _ -> false) |> should be True
    errs |> List.exists (function Validation.UnknownUnitRef v when v = "U_MISSING" -> true | _ -> false) |> should be True

[<Fact>]
let ``Validation detects invalid decimals attribute`` () =
    let args : BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025,3,31))
          Dimensions = []
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = [ { QName = Ns.jppfs + "NetSales"; ContextRef = "C1"; UnitRef = "U1"; Decimals = 0; Rounding = RoundingPolicy.Truncate; Value = 1m } ]
          SchemaRefHref = None }
    let doc = buildDocument args
    let fact = doc.Root.Element(Ns.jppfs + "NetSales")
    fact.SetAttributeValue(XName.Get "decimals", "NaN")
    let errs = Validation.validateDocument doc
    errs |> List.exists (function Validation.InvalidDecimals _ -> true | _ -> false) |> should be True

[<Property(MaxTest = 100)>]
let ``Validation flags missing prefixes when not declared`` () =
    let am: AxisMember =
        { Axis = Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
          Member = Ns.jppfs + "ConsolidatedMember" }

    let args: BuildArgs =
        { Lei = "5493001KJTIIGC8Y1R12"
          ContextId = "C1"
          Period = Period.Instant(DateTime(2025, 3, 31))
          Dimensions = [ am ]
          UnitId = "U1"
          MeasureQName = "iso4217:JPY"
          Facts = []
          SchemaRefHref = None }

    let prefixes: PrefixMap = Map.empty
    let doc = buildDocumentWithNamespaces prefixes args
    let errs = Validation.validateDocument doc

    errs
    |> List.exists (function
        | Validation.MissingNamespacePrefix _ -> true
        | _ -> false)
