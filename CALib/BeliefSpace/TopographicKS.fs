﻿module TopographicKS
//this version of TopographicalKS is based on Brainstorm Optimization method (BSO)
open CA
open CAUtils
open CAEvolve
open MachineLearning

let eSigma = 1.0

type Centroid =
    {
        Center  : float[]
        Count   : int
        Best    : float[]
        BestFit : float
    }

type CIndv = {CParms:float[]; CFitness:float}

type State<'a> = 
    {
        Centroids       : Centroid list
        CIndvs          : Marker array
        Fitness         : Fitness
        FitScaler       : float
        SpinWheel       : (Centroid*float)[]
        ParmDefs        : Parm[]
    }

let MAX_INDVS = 1000
let cfact xs k =  KMeansClustering.randomCentroids Probability.RNG.Value xs k |> List.map (fun (x:float[])->x,[])
let cdist (x,_) y = KMeansClustering.euclidean x y
let cavg (c,_) xs = (KMeansClustering.avgCentroid c xs),xs

let log cntrds = cntrds |> Seq.map (fun c -> c.Center) |> Seq.toList |> Metrics.MetricMsg.TopoState |> Metrics.postAll

let toCentroid state (c,members) =
    let lbest = members |> Seq.maxBy (fun ps -> (state.Fitness.Value ps) * state.FitScaler)
    {
        Center = c
        Count  = Seq.length members
        Best = lbest
        BestFit = state.Fitness.Value lbest
    }

let updateClusters state voters =
    let voters = voters |> Seq.map (fun indv -> toMarker indv)
    let vns = 
        Seq.append state.CIndvs voters 
        |> Seq.sortByDescending (fun i-> state.FitScaler * i.MFitness) 
        |> Seq.truncate MAX_INDVS 
        |> Seq.toArray
    let parmsArray = vns |> Array.map(fun i->i.MParms)
    // type CentroidsFactory<'a> = 'a seq -> int -> Centroid<'a> seq
    let k = match vns.Length with x when x < 10 -> 2 | x when x < 20 -> 4 | x when x < 100 -> 5 | x when x < 500 -> 7 | _ -> 10
    let kcntrods,_ = KMeansClustering.kmeans cdist cfact cavg  parmsArray k
    let cntrds = kcntrods |> Seq.filter (fun (_,ls)->List.isEmpty ls |> not) |> Seq.map (toCentroid state) |> Seq.toList
    let _,wheel = cntrds |> Seq.map (fun c->c,float c.Count) |> Seq.toArray |> Probability.createWheel

    #if _LOG_
    log cntrds
    #endif

    { state with Centroids = cntrds; SpinWheel=wheel}

let influenceIndv state s (indv:Individual<_>) =
    //mutation
    let cntrd = Probability.spinWheel state.SpinWheel 
    let p2 = cntrd.Best
    let updateParms = indv.Parms
    p2 |> Array.iteri (fun i p -> evolveP s eSigma updateParms i state.ParmDefs.[i] p)
    indv

let initialState parmDefs isBetter fitness =
    {
        Centroids   = []
        CIndvs      = [||]
        Fitness     = fitness
        FitScaler   = if isBetter 1. 0. then 1. else -1.
        SpinWheel   = [||]
        ParmDefs    = parmDefs
    }
    
let create parmDefs isBetter (fitness:Fitness) =
    let create state fAccept : KnowledgeSource<_> =
        {
            Type        = Topgraphical
            Accept      = fAccept state
            Influence   = influenceIndv state
        }

    let rec acceptance state envChanged  (voters:Individual<_> array) =
        let state = if envChanged then initialState parmDefs isBetter fitness else state
        let state = updateClusters state voters
        voters,create state acceptance 
           
    let state = initialState parmDefs isBetter fitness
    create state acceptance
