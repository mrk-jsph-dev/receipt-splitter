using System.Text.Json.Serialization;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.PatchBill;

// One op per PATCH, picked by "op". Every op that changes existing data carries "from": what the
// person saw. If the bill no longer matches it, nothing is written and the client gets 409 with the
// latest bill. See docs/SPEC.md "Two people, same item".

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddPersonOp), "addPerson")]
[JsonDerivedType(typeof(JoinOp), "join")]
[JsonDerivedType(typeof(RenameOp), "rename")]
[JsonDerivedType(typeof(RemovePersonOp), "removePerson")]
[JsonDerivedType(typeof(ClaimOp), "claim")]
[JsonDerivedType(typeof(UnclaimOp), "unclaim")]
[JsonDerivedType(typeof(SetWeightOp), "setWeight")]
[JsonDerivedType(typeof(MarkPaidOp), "markPaid")]
public record PatchOp;

/// <summary>Adds a friend, for example one without a phone. Two people can't share a name.</summary>
public sealed record AddPersonOp(string Name) : PatchOp;

/// <summary>Adds yourself. The browser remembers which person you are.</summary>
public sealed record JoinOp(string Name) : PatchOp;

/// <param name="From">The name as the client saw it.</param>
public sealed record RenameOp(string PersonId, string Name, string From) : PatchOp;

/// <summary>Only for someone the bill doesn't refer to anywhere yet: no claims, no payments, not a payer.</summary>
public sealed record RemovePersonOp(string PersonId, string From) : PatchOp;

/// <param name="From">The unit's claims as the client saw them. Order does not matter.</param>
/// <param name="Weight">1 for an equal share. 2 : 1 : 1 gives the first claimant half.</param>
public sealed record ClaimOp(string UnitId, string PersonId, IReadOnlyList<ClaimDoc> From, long Weight = 1) : PatchOp;

public sealed record UnclaimOp(string UnitId, string PersonId, IReadOnlyList<ClaimDoc> From) : PatchOp;

public sealed record SetWeightOp(string UnitId, string PersonId, long Weight, IReadOnlyList<ClaimDoc> From) : PatchOp;

/// <summary>"I've paid". Sets waiting; only the organizer can confirm.</summary>
/// <param name="From">The payment status the client saw, or null for none.</param>
public sealed record MarkPaidOp(string PersonId, PaymentStatus? From) : PatchOp;
