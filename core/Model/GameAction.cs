using System.Text.Json.Serialization;

namespace Game.Core;

/// <summary>Действие подготовки (12.2).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PlayCard), "play_card")]
[JsonDerivedType(typeof(UseAbility), "use_ability")]
[JsonDerivedType(typeof(Move), "move")]
[JsonDerivedType(typeof(Recall), "recall")]
[JsonDerivedType(typeof(Dismiss), "dismiss")]
[JsonDerivedType(typeof(Ready), "ready")]
public abstract record GameAction;

/// <param name="Slot">Позиция на столе или слот поля (занятый слот — замена).</param>
/// <param name="Target">entityId выбранного существа.</param>
public sealed record PlayCard(int InstanceId, int? Slot = null, int? Target = null) : GameAction;

public sealed record UseAbility(int EntityId, int AbilityIndex, int? Target = null) : GameAction;

public sealed record Move(int EntityId, int ToIndex) : GameAction;

public sealed record Recall(int EntityId) : GameAction;

public sealed record Dismiss(int EntityId) : GameAction;

public sealed record Ready : GameAction;
