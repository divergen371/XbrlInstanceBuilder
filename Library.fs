namespace Xbrl

open System
open System.Xml.Linq
open System.IO
open System.Text.Json
open System.Collections.Generic

module InstanceBuilder =

    module Ns =
        let xbrli = XNamespace.Get "http://www.xbrl.org/2003/instance"
        let xbrldi = XNamespace.Get "http://www.xbrl.org/2006/xbrldi"
        let iso4217 = XNamespace.Get "http://www.xbrl.org/2003/iso4217"
        let link = XNamespace.Get "http://www.xbrl.org/2003/linkbase"
        let xlink = XNamespace.Get "http://www.w3.org/1999/xlink"
        // タクソノミ日付は運用日で変える。ここでは2020-11-01系(EDINET公開物の代表例)
        let jppfs =
            XNamespace.Get
                "http://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor"

        let jpcrp =
            XNamespace.Get
                "http://disclosure.edinet-fsa.go.jp/taxonomy/jpcrp/2020-11-01/jpcrp_cor"
        // jpdeiはタクソノミ日付可変規約。必要に応じて差し替え(規約書にパターン定義あり)
        let jpdei =
            XNamespace.Get
                "http://disclosure.edinet-fsa.go.jp/taxonomy/jpdei/2020-11-01/jpdei_cor"

    /// 設定ファイルによる schemaRef 既定の指定をサポート
    module Config =
        /// エントリポイント種別
        type EntryPoint =
            | ASR
            | QSR
            | SRS of variant: string option
            | SSR of variant: string option
            | JPPFS of industry: string
            | JPDEI

        /// 設定レコード
        type Settings =
            { TaxonomyDate: string
              EntryPoint: EntryPoint }

        /// 設定に基づいて schemaRef の完全URLを生成
        let schemaRefUrl (s: Settings) =
            let date = s.TaxonomyDate

            let build prefix file =
                $"https://disclosure.edinet-fsa.go.jp/taxonomy/%s{prefix}/%s{date}/%s{file}"

            match s.EntryPoint with
            | ASR -> build "jpcrp" $"entryPoint_jpcrp040000-asr_%s{date}.xsd"
            | QSR -> build "jpcrp" $"entryPoint_jpcrp040300-qsr_%s{date}.xsd"
            | SRS variant ->
                let v = defaultArg variant "srs"

                build "jpcrp" $"entryPoint_jpcrp070000-%s{v}_%s{date}.xsd"
            | SSR variant ->
                let v = defaultArg variant "ssr"

                build "jpcrp" $"entryPoint_jpcrp050000-%s{v}_%s{date}.xsd"
            | JPPFS industry ->
                build "jppfs" $"entryPoint_jppfs_%s{industry}_%s{date}.xsd"
            | JPDEI -> build "jpdei" $"entryPoint_jpdei_%s{date}.xsd"

        /// JSON 設定を読み込みます。
        /// 例:
        /// {
        ///   "taxonomyDate": "2020-11-01",
        ///   "entryPoint": { "kind": "ASR" }
        /// }
        /// JPPFS の例:
        /// {
        ///   "taxonomyDate": "2020-11-01",
        ///   "entryPoint": { "kind": "JPPFS", "industry": "cai" }
        /// }
        let tryLoad (pathOpt: string option) : Settings option =
            try
                let path =
                    match pathOpt with
                    | Some p when File.Exists p -> p
                    | _ ->
                        match
                            Environment.GetEnvironmentVariable
                                "XBRL_INSTANCEBUILDER_CONFIG"
                        with
                        | null
                        | "" ->
                            let candidate =
                                Path.Combine(
                                    Directory.GetCurrentDirectory(),
                                    "xbrl.instancebuilder.json"
                                )

                            if File.Exists candidate then candidate else null
                        | p -> if File.Exists p then p else null

                if String.IsNullOrEmpty path then
                    None
                else
                    use doc = JsonDocument.Parse(File.ReadAllText path)
                    let root = doc.RootElement

                    let taxonomyDate =
                        match root.TryGetProperty "taxonomyDate" with
                        | true, v when v.ValueKind = JsonValueKind.String ->
                            v.GetString()
                        | _ -> null

                    let ep =
                        match root.TryGetProperty "entryPoint" with
                        | true, epEl ->
                            let kind =
                                match epEl.TryGetProperty "kind" with
                                | true, k when
                                    k.ValueKind = JsonValueKind.String
                                    ->
                                    k.GetString()
                                | _ -> null

                            match kind with
                            | null -> None
                            | "ASR" -> Some ASR
                            | "QSR" -> Some QSR
                            | "SRS" ->
                                let variant =
                                    match epEl.TryGetProperty "variant" with
                                    | true, v when
                                        v.ValueKind = JsonValueKind.String
                                        ->
                                        Some(v.GetString())
                                    | _ -> None

                                Some(SRS variant)
                            | "SSR" ->
                                let variant =
                                    match epEl.TryGetProperty "variant" with
                                    | true, v when
                                        v.ValueKind = JsonValueKind.String
                                        ->
                                        Some(v.GetString())
                                    | _ -> None

                                Some(SSR variant)
                            | "JPPFS" ->
                                let ind =
                                    match epEl.TryGetProperty "industry" with
                                    | true, v when
                                        v.ValueKind = JsonValueKind.String
                                        ->
                                        v.GetString()
                                    | _ -> null

                                if String.IsNullOrEmpty ind then
                                    None
                                else
                                    Some(JPPFS ind)
                            | "JPDEI" -> Some JPDEI
                            | _ -> None
                        | _ -> None

                    if String.IsNullOrEmpty taxonomyDate then
                        None
                    else
                        match ep with
                        | Some epv ->
                            Some
                                { TaxonomyDate = taxonomyDate
                                  EntryPoint = epv }
                        | None -> None
            with _ ->
                None

        /// 既定設定から schemaRef の URL を取得します（環境変数やカレントの json を探索）
        let tryGetDefaultSchemaRefHref
            (pathOpt: string option)
            : string option
            =
            tryLoad pathOpt |> Option.map schemaRefUrl

    /// エントリポイント設定のプリセットヘルパー
    module Preset =
        open Config

        let private fmt (d: DateTime) = d.ToString("yyyy-MM-dd")

        // --- ASR / QSR / SRS / SSR ---
        let epAsr (taxonomyDate: string) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.ASR }

        let epAsrDt (taxonomyDate: DateTime) : Settings =
            epAsr (fmt taxonomyDate)

        let qsr (taxonomyDate: string) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.QSR }

        let qsrDt (taxonomyDate: DateTime) : Settings = qsr (fmt taxonomyDate)

        let srs (taxonomyDate: string) (variant: string option) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.SRS variant }

        let srsDt (taxonomyDate: DateTime) (variant: string option) : Settings =
            srs (fmt taxonomyDate) variant

        let ssr (taxonomyDate: string) (variant: string option) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.SSR variant }

        let ssrDt (taxonomyDate: DateTime) (variant: string option) : Settings =
            ssr (fmt taxonomyDate) variant

        // --- JPPFS（業種別） ---
        let jppfs (taxonomyDate: string) (industry: string) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.JPPFS industry }

        let jppfsDt (taxonomyDate: DateTime) (industry: string) : Settings =
            jppfs (fmt taxonomyDate) industry

        // --- JPDEI ---
        let jpdei (taxonomyDate: string) : Settings =
            { TaxonomyDate = taxonomyDate
              EntryPoint = EntryPoint.JPDEI }

        let jpdeiDt (taxonomyDate: DateTime) : Settings =
            jpdei (fmt taxonomyDate)

    let attr (name: XName) (value: obj) = XAttribute(name, value)

    let elem (name: XName) (children: obj list) =
        XElement(name, children |> List.toArray)

    let qnameOf (xname: XName) =
        let ns = xname.Namespace

        if ns = Ns.jppfs then $"jppfs_cor:{xname.LocalName}"
        elif ns = Ns.jpcrp then $"jpcrp_cor:{xname.LocalName}"
        elif ns = Ns.jpdei then $"jpdei_cor:{xname.LocalName}"
        else xname.LocalName

    /// 任意のプレフィックスマップ（namespaceUri(string) -> prefix）を使って QName を生成
    type PrefixMap = Map<string, string>

    let qnameOfWith (prefixes: PrefixMap) (xname: XName) =
        let nsUri = string xname.Namespace

        match prefixes |> Map.tryFind nsUri with
        | Some p -> $"{p}:{xname.LocalName}"
        | None -> qnameOf xname

    /// ディメンション: 軸とメンバを XName で表現
    [<CLIMutable>]
    type AxisMember = { Axis: XName; Member: XName }

    type Period =
        | Instant of DateTime
        | Duration of startDate: DateTime * endDate: DateTime

    let context
        (id: string)
        (lei: string)
        (period: Period)
        (dim: AxisMember list)
        =
        let periodEl =
            match period with
            | Instant d ->
                elem
                    (Ns.xbrli + "period")
                    [ elem
                          (Ns.xbrli + "instant")
                          [ box (d.ToString("yyyy-MM-dd")) ] ]
            | Duration(s, e) ->
                elem
                    (Ns.xbrli + "period")
                    [ elem
                          (Ns.xbrli + "startDate")
                          [ box (s.ToString("yyyy-MM-dd")) ]
                      elem
                          (Ns.xbrli + "endDate")
                          [ box (e.ToString("yyyy-MM-dd")) ] ]

        let segment =
            if List.isEmpty dim then
                null
            else
                elem
                    (Ns.xbrli + "segment")
                    [ yield!
                          dim
                          |> List.map (fun am ->
                              box (
                                  elem
                                      (Ns.xbrldi + "explicitMember")
                                      [ attr
                                            (XName.Get "dimension")
                                            (box (qnameOf am.Axis))
                                        box (qnameOf am.Member) ]
                              )) ]

        elem
            (Ns.xbrli + "context")
            [ attr (XName.Get "id") id
              elem
                  (Ns.xbrli + "entity")
                  [ elem
                        (Ns.xbrli + "identifier")
                        [ attr (XName.Get "scheme") "http://www.gleif.org/lei"
                          box lei ] ]
              box periodEl
              if box segment <> null then
                  box segment ]

    let unitElem (id: string) (measureQName: string) =
        elem
            (Ns.xbrli + "unit")
            [ attr (XName.Get "id") id
              elem (Ns.xbrli + "measure") [ box measureQName ] ]

    /// PrefixMap を使った context（explicitMember の dimension/member を指定プレフィックスで出力）
    let contextWithPrefixMap
        (prefixes: PrefixMap)
        (id: string)
        (lei: string)
        (period: Period)
        (dim: AxisMember list)
        =
        let periodEl =
            match period with
            | Instant d ->
                elem
                    (Ns.xbrli + "period")
                    [ elem
                          (Ns.xbrli + "instant")
                          [ box (d.ToString("yyyy-MM-dd")) ] ]
            | Duration(s, e) ->
                elem
                    (Ns.xbrli + "period")
                    [ elem
                          (Ns.xbrli + "startDate")
                          [ box (s.ToString("yyyy-MM-dd")) ]
                      elem
                          (Ns.xbrli + "endDate")
                          [ box (e.ToString("yyyy-MM-dd")) ] ]

        let segment =
            if List.isEmpty dim then
                null
            else
                elem
                    (Ns.xbrli + "segment")
                    [ yield!
                          dim
                          |> List.map (fun am ->
                              box (
                                  elem
                                      (Ns.xbrldi + "explicitMember")
                                      [ attr
                                            (XName.Get "dimension")
                                            (box (qnameOfWith prefixes am.Axis))
                                        box (qnameOfWith prefixes am.Member) ]
                              )) ]

        elem
            (Ns.xbrli + "context")
            [ attr (XName.Get "id") id
              elem
                  (Ns.xbrli + "entity")
                  [ elem
                        (Ns.xbrli + "identifier")
                        [ attr (XName.Get "scheme") "http://www.gleif.org/lei"
                          box lei ] ]
              box periodEl
              if box segment <> null then
                  box segment ]

    let monetaryFact
        (qname: XName)
        (contextRef: string)
        (unitRef: string)
        (decimals: int)
        (value: decimal)
        =
        elem
            qname
            [ attr (XName.Get "contextRef") contextRef
              attr (XName.Get "unitRef") unitRef
              attr (XName.Get "decimals") (string decimals)
              box value ]

    /// 丸めポリシー
    type RoundingPolicy =
        | Truncate // 0 方向に丸め
        | RoundHalfUp // 四捨五入（0.5 は絶対値の大きい方向）
        | RoundDown // 負方向へ丸め（床）

    let private pow10 (n: int) : decimal =
        if n <= 0 then
            1m
        else
            let mutable acc = 1m

            for _i in 1..n do
                acc <- acc * 10m

            acc

    let private roundWith
        (policy: RoundingPolicy)
        (decimals: int)
        (value: decimal)
        : decimal
        =
        match policy with
        | RoundHalfUp ->
            if decimals >= 0 then
                Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            else
                let f = pow10 (-decimals)
                Math.Round(value / f, 0, MidpointRounding.AwayFromZero) * f
        | Truncate ->
            if decimals >= 0 then
                let scale = pow10 decimals
                let scaled = value * scale

                let truncated =
                    if scaled >= 0m then
                        Math.Floor scaled
                    else
                        Math.Ceiling scaled

                truncated / scale
            else
                let f = pow10 (-decimals)
                let scaled = value / f

                let truncated =
                    if scaled >= 0m then
                        Math.Floor scaled
                    else
                        Math.Ceiling scaled

                truncated * f
        | RoundDown ->
            if decimals >= 0 then
                let scale = pow10 decimals
                Math.Floor(value * scale) / scale
            else
                let f = pow10 (-decimals)
                Math.Floor(value / f) * f

    /// パラメータ: 金額ファクト
    [<CLIMutable>]
    type MonetaryFactArg =
        { QName: XName
          ContextRef: string
          UnitRef: string
          Decimals: int
          Rounding: RoundingPolicy
          Value: decimal }

    /// XBRL インスタンス生成の入力引数
    [<CLIMutable>]
    type BuildArgs =
        { Lei: string
          ContextId: string
          Period: Period
          Dimensions: AxisMember list
          UnitId: string
          MeasureQName: string
          Facts: MonetaryFactArg list
          SchemaRefHref: string option }

    /// 引数から XBRL インスタンス文書 (XDocument) を生成します。
    /// SchemaRefHref が None の場合、既定で 2020-11-01 の jppfs エントリポイント
    /// "https://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd"
    /// を link:schemaRef として付与します。
    let buildDocument (args: BuildArgs) =
        let ctx = context args.ContextId args.Lei args.Period args.Dimensions
        let u = unitElem args.UnitId args.MeasureQName

        let facts =
            args.Facts
            |> List.map (fun f ->
                let rounded = roundWith f.Rounding f.Decimals f.Value

                monetaryFact f.QName f.ContextRef f.UnitRef f.Decimals rounded
                |> box)

        let root =
            elem
                (Ns.xbrli + "xbrl")
                [
                  // 名前空間宣言
                  attr (XNamespace.Xmlns + "xbrli") (string Ns.xbrli)
                  attr (XNamespace.Xmlns + "xbrldi") (string Ns.xbrldi)
                  attr (XNamespace.Xmlns + "iso4217") (string Ns.iso4217)
                  attr (XNamespace.Xmlns + "jppfs_cor") (string Ns.jppfs)
                  attr (XNamespace.Xmlns + "jpcrp_cor") (string Ns.jpcrp)
                  attr (XNamespace.Xmlns + "jpdei_cor") (string Ns.jpdei)
                  attr (XNamespace.Xmlns + "link") (string Ns.link)
                  attr (XNamespace.Xmlns + "xlink") (string Ns.xlink)
                  let toHttps (url: string) =
                      if
                          url.StartsWith("http://disclosure.edinet-fsa.go.jp")
                      then
                          "https://" + url.Substring("http://".Length)
                      else
                          url

                  match args.SchemaRefHref with
                  | Some href ->
                      box (
                          elem
                              (Ns.link + "schemaRef")
                              [ attr (Ns.xlink + "type") "simple"
                                attr (Ns.xlink + "href") (toHttps href) ]
                      )
                  | None ->
                      box (
                          elem
                              (Ns.link + "schemaRef")
                              [ attr (Ns.xlink + "type") "simple"
                                attr
                                    (Ns.xlink + "href")
                                    "https://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd" ]
                      )

                  box ctx
                  box u
                  yield! facts ]

        XDocument(root)

    /// Namespace のプレフィックスを外部から与えて文書を生成
    let buildDocumentWithNamespaces (prefixes: PrefixMap) (args: BuildArgs) =
        let ctx =
            contextWithPrefixMap
                prefixes
                args.ContextId
                args.Lei
                args.Period
                args.Dimensions

        let u = unitElem args.UnitId args.MeasureQName

        let facts =
            args.Facts
            |> List.map (fun f ->
                let rounded = roundWith f.Rounding f.Decimals f.Value

                monetaryFact f.QName f.ContextRef f.UnitRef f.Decimals rounded
                |> box)

        let toHttps (url: string) =
            if url.StartsWith("http://disclosure.edinet-fsa.go.jp") then
                "https://" + url.Substring("http://".Length)
            else
                url

        let schemaRefHref =
            args.SchemaRefHref
            |> Option.defaultValue
                "https://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd"
            |> toHttps
        // ルート要素
        let rootChildren: obj list =
            [
              // 既定の必須名前空間
              box (attr (XNamespace.Xmlns + "xbrli") (string Ns.xbrli))
              box (attr (XNamespace.Xmlns + "xbrldi") (string Ns.xbrldi))
              box (attr (XNamespace.Xmlns + "iso4217") (string Ns.iso4217))
              box (attr (XNamespace.Xmlns + "link") (string Ns.link))
              box (attr (XNamespace.Xmlns + "xlink") (string Ns.xlink)) ]
            @ (prefixes
               |> Map.toList
               |> List.map (fun (nsUri, prefix) ->
                   box (attr (XNamespace.Xmlns + prefix) nsUri)))

        let root =
            elem
                (Ns.xbrli + "xbrl")
                (rootChildren
                 @ [ box (
                         elem
                             (Ns.link + "schemaRef")
                             [ attr (Ns.xlink + "type") "simple"
                               attr (Ns.xlink + "href") schemaRefHref ]
                     )
                     box ctx
                     box u
                     yield! facts ])

        XDocument(root)

    /// 設定ファイル（JSON）のパスをオプションで与えて、schemaRef の解決を行ってから文書を生成します。
    /// - args.SchemaRefHref が Some の場合はそれを優先します。
    /// - None の場合、configPathOpt をもとに既定 schemaRef を試行し、なければ buildDocument の既定にフォールバックします。
    let buildDocumentWithConfig
        (configPathOpt: string option)
        (args: BuildArgs)
        =
        let resolvedArgs =
            match args.SchemaRefHref with
            | Some _ -> args
            | None ->
                match Config.tryGetDefaultSchemaRefHref configPathOpt with
                | Some href -> { args with SchemaRefHref = Some href }
                | None -> args

        buildDocument resolvedArgs

    /// スキーマ参照解決のための汎用リゾルバ
    module Resolver =
        /// schemaRef の URL を返す関数型（None の場合は未解決）
        type SchemaRefResolver = unit -> string option

        /// JSON 設定ファイル/環境変数/カレントの規約で解決するリゾルバ
        let fromConfigFile (pathOpt: string option) : SchemaRefResolver =
            fun () -> Config.tryGetDefaultSchemaRefHref pathOpt

        /// 固定URLを返すリゾルバ
        let fromConst (href: string) : SchemaRefResolver = fun () -> Some href

        /// 何も返さないリゾルバ
        let empty: SchemaRefResolver = fun () -> None

    /// リゾルバで schemaRef を解決してから文書を生成します。
    /// - args.SchemaRefHref が Some の場合はそれを優先します。
    /// - None の場合、resolver() の結果を用います（None なら buildDocument の既定）。
    let buildDocumentWithResolver
        (resolver: Resolver.SchemaRefResolver)
        (args: BuildArgs)
        =
        let resolved =
            match args.SchemaRefHref with
            | Some _ -> args
            | None ->
                match resolver () with
                | Some href -> { args with SchemaRefHref = Some href }
                | None -> args

        buildDocument resolved

    /// 検証エラー
    type BuildError =
        | MissingLei
        | EmptyContextId
        | InvalidDimensions of string

    /// 入力検証を行い、XDocument を生成します（失敗時はエラーの一覧を返します）。
    let tryBuildDocument
        (args: BuildArgs)
        : Result<XDocument, BuildError list>
        =
        let errs =
            [ if String.IsNullOrWhiteSpace args.Lei then
                  MissingLei
              if String.IsNullOrWhiteSpace args.ContextId then
                  EmptyContextId
              if
                  args.Dimensions
                  |> List.exists (fun am ->
                      isNull (box am.Axis) || isNull (box am.Member))
              then
                  InvalidDimensions "Axis or Member is null" ]

        match errs with
        | [] -> Ok(buildDocument args)
        | es -> Error es

    /// C# フレンドリーな結果型
    [<CLIMutable>]
    type BuildOutcome =
        { Success: bool
          Document: XDocument
          Errors: string array }

    /// C# フレンドリー API
    module CSharp =
        let private toErrorStrings (errs: BuildError list) =
            errs
            |> List.map (function
                | MissingLei -> "MissingLei"
                | EmptyContextId -> "EmptyContextId"
                | InvalidDimensions msg -> $"InvalidDimensions: {msg}")
            |> List.toArray

        /// AxisMember を生成
        let newAxisMember (axis: XName) (memberName: XName) : AxisMember =
            { Axis = axis; Member = memberName }

        /// MonetaryFactArg を生成
        let newMonetaryFactArg
            (qname: XName)
            (contextRef: string)
            (unitRef: string)
            (decimals: int)
            (rounding: RoundingPolicy)
            (value: decimal)
            : MonetaryFactArg
            =
            { QName = qname
              ContextRef = contextRef
              UnitRef = unitRef
              Decimals = decimals
              Rounding = rounding
              Value = value }

        /// Result を BuildOutcome に変換
        let private outcomeOf
            (r: Result<XDocument, BuildError list>)
            : BuildOutcome
            =
            match r with
            | Ok doc ->
                { Success = true
                  Document = doc
                  Errors = Array.empty }
            | Error es ->
                { Success = false
                  Document = null
                  Errors = toErrorStrings es }

        /// 検証つきビルド
        let tryBuild (args: BuildArgs) : BuildOutcome =
            outcomeOf (tryBuildDocument args)

        /// 設定パスを用いたビルド
        let buildWithConfigPath
            (configPath: string)
            (args: BuildArgs)
            : XDocument
            =
            buildDocumentWithConfig (Some configPath) args

        /// 任意のプレフィックス辞書（Dictionary<string,string>）でビルド
        let buildWithNamespaces
            (prefixDict: IDictionary<string, string>)
            (args: BuildArgs)
            : XDocument
            =
            let mp =
                prefixDict |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

            buildDocumentWithNamespaces mp args

    /// バリデーション（軽量な構文的チェック）
    module Validation =
        type ValidationError =
            | MissingNamespacePrefix of prefix: string
            | MissingSchemaRef
            | InvalidExplicitMember of detail: string
            | MissingContext
            | MissingUnit
            | DuplicateContextId of string
            | DuplicateUnitId of string
            | UnknownContextRef of string
            | UnknownUnitRef of string
            | InvalidDecimals of string

        /// XDocument を検査し、未宣言プレフィックスや schemaRef 欠如、
        /// context/unit の不足・重複、fact の参照不整合などを検出
        let validateDocument (doc: XDocument) : ValidationError list =
            let root = doc.Root

            if isNull (box root) then
                []
            else
                let xmlnsPrefixes =
                    root.Attributes()
                    |> Seq.choose (fun a ->
                        if a.IsNamespaceDeclaration then
                            let name = a.Name.LocalName // "xmlns:prefix" -> LocalName = prefix, or "xmlns"
                            if name = "xmlns" then None else Some name
                        else
                            None)
                    |> Set.ofSeq

                let schemaRef = root.Element(Ns.link + "schemaRef")
                let errs = ResizeArray<_>()

                if isNull (box schemaRef) then
                    errs.Add MissingSchemaRef

                // explicitMember: check dimension attr and inner text have prefix that is declared
                let ems = root.Descendants(Ns.xbrldi + "explicitMember")

                for em in ems do
                    let dimAttr = em.Attribute(XName.Get "dimension")

                    if not (isNull (box dimAttr)) then
                        let v = dimAttr.Value

                        match v.Split(':') with
                        | [| pfx; _ |] ->
                            if not (xmlnsPrefixes.Contains pfx) then
                                errs.Add(MissingNamespacePrefix pfx)
                        | _ ->
                            errs.Add(
                                InvalidExplicitMember(
                                    "dimension should be QName: " + v
                                )
                            )

                    let content = em.Value

                    match content.Split(':') with
                    | [| pfx; _ |] ->
                        if not (xmlnsPrefixes.Contains pfx) then
                            errs.Add(MissingNamespacePrefix pfx)
                    | _ ->
                        errs.Add(
                            InvalidExplicitMember(
                                "member should be QName: " + content
                            )
                        )

                // contexts / units: presence and duplicate id check
                let contexts = root.Elements(Ns.xbrli + "context") |> Seq.toList
                let units = root.Elements(Ns.xbrli + "unit") |> Seq.toList

                if List.isEmpty contexts then
                    errs.Add MissingContext

                if List.isEmpty units then
                    errs.Add MissingUnit

                let idOf (el: XElement) =
                    let a = el.Attribute(XName.Get "id")
                    if isNull (box a) then None else Some a.Value

                let dupIds (els: XElement list) =
                    els
                    |> List.choose idOf
                    |> List.groupBy id
                    |> List.choose (fun (k, vs) ->
                        if List.length vs > 1 then Some k else None)

                for id in dupIds contexts do
                    errs.Add(DuplicateContextId id)

                for id in dupIds units do
                    errs.Add(DuplicateUnitId id)

                // fact references: unknown contextRef/unitRef and invalid decimals
                let ctxIdSet = contexts |> Seq.choose idOf |> Set.ofSeq

                let unitIdSet = units |> Seq.choose idOf |> Set.ofSeq

                let allEls = root.Descendants() |> Seq.toList

                for el in allEls do
                    let cref = el.Attribute(XName.Get "contextRef")

                    if not (isNull (box cref)) then
                        let v = cref.Value

                        if not (ctxIdSet.Contains v) then
                            errs.Add(UnknownContextRef v)

                    let uref = el.Attribute(XName.Get "unitRef")

                    if not (isNull (box uref)) then
                        let v = uref.Value

                        if not (unitIdSet.Contains v) then
                            errs.Add(UnknownUnitRef v)

                    let dec = el.Attribute(XName.Get "decimals")

                    if not (isNull (box dec)) then
                        let mutable parsed = 0

                        if not (Int32.TryParse(dec.Value, &parsed)) then
                            errs.Add(InvalidDecimals dec.Value)

                List.ofSeq errs

    /// ドキュメントビルダー: Context と Unit を重複排除し、ID を自動採番する
    module Builder =
        /// 内部キー生成（コンテキスト）
        let private periodKey =
            function
            | Instant d -> sprintf "I:%s" (d.ToString("yyyy-MM-dd"))
            | Duration(s, e) ->
                sprintf
                    "D:%s..%s"
                    (s.ToString("yyyy-MM-dd"))
                    (e.ToString("yyyy-MM-dd"))

        let private dimsKey (dims: AxisMember list) =
            dims
            |> List.map (fun am ->
                (qnameOf am.Axis) + "=" + (qnameOf am.Member))
            |> List.sort
            |> String.concat ";"

        type private CKey = string // periodKey + "|" + dimsKey

        type DocBuilder =
            { Lei: string
              SchemaRefHref: string option
              Contexts: Map<CKey, string * XElement>
              Units: Map<string, string * XElement> // key = measureQName
              Facts: XElement list
              ContextSeq: int
              UnitSeq: int }

        /// ビルダー生成
        let init (lei: string) (schemaRefHref: string option) : DocBuilder =
            { Lei = lei
              SchemaRefHref = schemaRefHref
              Contexts = Map.empty
              Units = Map.empty
              Facts = []
              ContextSeq = 0
              UnitSeq = 0 }

        /// コンテキストを追加（既存があれば再利用）。戻り値は更新後ビルダーと contextId
        let addContext
            (period: Period)
            (dims: AxisMember list)
            (b: DocBuilder)
            : DocBuilder * string
            =
            let key: CKey = (periodKey period) + "|" + (dimsKey dims)

            match b.Contexts |> Map.tryFind key with
            | Some(cid, _el) -> b, cid
            | None ->
                let newId = $"C{b.ContextSeq + 1}"
                let el = context newId b.Lei period dims
                let ctxs = b.Contexts |> Map.add key (newId, el)

                { b with
                    Contexts = ctxs
                    ContextSeq = b.ContextSeq + 1 },
                newId

        /// ユニットを追加（既存があれば再利用）。戻り値は更新後ビルダーと unitId
        let addUnit
            (measureQName: string)
            (b: DocBuilder)
            : DocBuilder * string
            =
            match b.Units |> Map.tryFind measureQName with
            | Some(uid, _el) -> b, uid
            | None ->
                let newId = $"U{b.UnitSeq + 1}"
                let el = unitElem newId measureQName
                let units = b.Units |> Map.add measureQName (newId, el)

                { b with
                    Units = units
                    UnitSeq = b.UnitSeq + 1 },
                newId

        /// 参照ID指定でファクトを追加
        let addFactWithRefs
            (qname: XName)
            (contextRef: string)
            (unitRef: string)
            (decimals: int)
            (rounding: RoundingPolicy)
            (value: decimal)
            (b: DocBuilder)
            : DocBuilder
            =
            let v = roundWith rounding decimals value
            let fact = monetaryFact qname contextRef unitRef decimals v
            { b with Facts = b.Facts @ [ fact ] }

        /// Period/Dimension/Measure から自動で Context/Unit を確保してファクトを追加
        let addFact
            (qname: XName)
            (period: Period)
            (dims: AxisMember list)
            (measureQName: string)
            (decimals: int)
            (rounding: RoundingPolicy)
            (value: decimal)
            (b: DocBuilder)
            : DocBuilder * string * string
            =
            let b1, cid = addContext period dims b
            let b2, uid = addUnit measureQName b1
            let b3 = addFactWithRefs qname cid uid decimals rounding value b2
            b3, cid, uid

        /// 追加済みの要素で XBRL ドキュメントを構築
        let build (b: DocBuilder) : XDocument =
            let ctxEls =
                b.Contexts
                |> Map.toList
                |> List.map (fun (_k, (id, el)) -> id, el)
                |> List.sortBy fst
                |> List.map snd

            let unitEls =
                b.Units
                |> Map.toList
                |> List.map (fun (_k, (id, el)) -> id, el)
                |> List.sortBy fst
                |> List.map snd

            let toHttps (url: string) =
                if url.StartsWith("http://disclosure.edinet-fsa.go.jp") then
                    "https://" + url.Substring("http://".Length)
                else
                    url

            let schemaRefHref =
                b.SchemaRefHref
                |> Option.map toHttps
                |> Option.defaultValue
                    "https://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd"

            let root =
                elem
                    (Ns.xbrli + "xbrl")
                    [ attr (XNamespace.Xmlns + "xbrli") (string Ns.xbrli)
                      attr (XNamespace.Xmlns + "xbrldi") (string Ns.xbrldi)
                      attr (XNamespace.Xmlns + "iso4217") (string Ns.iso4217)
                      attr (XNamespace.Xmlns + "jppfs_cor") (string Ns.jppfs)
                      attr (XNamespace.Xmlns + "jpcrp_cor") (string Ns.jpcrp)
                      attr (XNamespace.Xmlns + "jpdei_cor") (string Ns.jpdei)
                      attr (XNamespace.Xmlns + "link") (string Ns.link)
                      attr (XNamespace.Xmlns + "xlink") (string Ns.xlink)
                      box (
                          elem
                              (Ns.link + "schemaRef")
                              [ attr (Ns.xlink + "type") "simple"
                                attr (Ns.xlink + "href") schemaRefHref ]
                      )
                      yield! ctxEls |> List.map box
                      yield! unitEls |> List.map box
                      yield! b.Facts |> List.map box ]

            XDocument(root)
