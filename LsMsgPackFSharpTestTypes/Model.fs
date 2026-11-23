/// The F# types of the tests (LsMsgPackUnitTests.FSharpTests): how F# compiles them is what the serializers recognize.
/// Names start with Fs: type ids are short names, unique among the assemblies the tests cache.
namespace LsMsgPackFSharpTestTypes

open System

type FsAddress = { Street: string; Number: int }

[<Struct>]
type FsPoint = { X: float; Y: float }

[<CLIMutable>]
type FsMutable = { A: int; B: string }

/// A union with fields: a class per case with fields, the fieldless case Empty an instance of the nested class _Empty
type FsShape =
  | Circle of radius: float
  | Rect of width: float * height: float
  | Empty

/// A union of fieldless cases (compiled as one class with a tag)
type FsColour = Red | Green | Blue

[<Struct>]
type FsStructColour = Up | Down

/// Six cases: the fieldless ones are instances of FsBig itself, E a nested class
type FsBig = A | B | C | D | E of int | F

/// One case with one field: written as the field
type FsEmail = FsEmail of string

[<Struct>]
type FsOrderId = FsOrderId of Guid

/// One case with several fields: an object of them
type FsPair = FsPair of left: int * right: string

/// Unnamed fields are Item, Item1, Item2
type FsUnnamed =
  | One of int
  | Two of int * string
  | Nothing

type FsGeneric<'T> =
  | Value of 'T
  | NoValue

/// A case name another union has too (Ok): resolved among the cases of the declared union
type FsOutcome =
  | Ok of code: int
  | Failed of reason: string

type FsAnswer =
  | Ok of text: string
  | Later

[<RequireQualifiedAccess>]
type FsQualified =
  | First
  | Second of int

/// A recursive union
type FsTree =
  | Leaf of int
  | Node of FsTree list

[<Struct>]
type FsStructShape =
  | SCircle of r: float
  | SSquare of s: float

type FsPerson =
  { Name: string
    Nick: string option
    Age: int voption
    Home: FsAddress option
    Score: float option }

type FsHolder =
  { Shapes: FsShape list
    Tags: Set<string>
    Scores: Map<string, int>
    Pair: int * string
    Colour: FsColour
    Mail: FsEmail
    Maybe: FsShape option
    Choice: Choice<int, string>
    Many: FsBig array }

type FsWithOutcome =
  { Outcome: FsOutcome
    Answer: FsAnswer
    Generic: FsGeneric<string>
    Unnamed: FsUnnamed list
    Tree: FsTree
    Qualified: FsQualified
    Id: FsOrderId
    StructColour: FsStructColour
    Nested: int option option }

type FsWithResult = { Result: Result<int, string> }

type FsWithStructUnion = { Shape: FsStructShape }

type FsWithObject = { Value: obj }

/// A row of LsMsgPack.Data's row mapper (LsMsgPack.DataTests.FSharpRowsTests): created by its constructor, options where a column can be NULL, unions from columns
type FsInvoiceRow =
  { Id: int
    Customer: string
    Note: string option
    Discount: decimal option
    Paid: bool voption
    Total: float
    Colour: FsColour
    Mail: FsEmail
    OrderId: FsOrderId
    Created: DateTime }

/// The same with a parameterless constructor and setters: set per column
[<CLIMutable>]
type FsMutableRow =
  { Id: int
    Customer: string
    Note: string option
    Discount: decimal option
    Paid: bool voption
    Total: float
    Colour: FsColour
    Mail: FsEmail
    OrderId: FsOrderId
    Created: DateTime }

module FsValues =
  let shapes = [ Circle 2.0; Rect(1.0, 3.5); Empty ]
