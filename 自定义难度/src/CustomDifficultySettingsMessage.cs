using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace CustomDifficulty;

public sealed class CustomDifficultySettingsMessage : INetMessage, IPacketSerializable
{
	public byte HpTicks { get; set; }

	public byte AttackTicks { get; set; }

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteByte(HpTicks);
		writer.WriteByte(AttackTicks);
	}

	public void Deserialize(PacketReader reader)
	{
		HpTicks = reader.ReadByte();
		AttackTicks = reader.ReadByte();
	}
}
