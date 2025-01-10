using RoR2;
using UnityEngine.Networking;

namespace CyAn_Rnd
{
    public class CyAn_Network : MessageBase
    {
        public int MsgType { get; private set; }
        public ItemIndex Item { get; private set; }
        public float CellZoneSize { get; private set; }

        public CyAn_Network() { } //this was present in the reference i used so i am using this too. I have no idea how networking stuff works, but it would not work any other way i tried (been 3 days at this point) so yeah

        public CyAn_Network(ItemIndex passItem)
        {
            MsgType = 0;
            Item = passItem;
        }

        public CyAn_Network(float size)
        {
            MsgType = 1;
            CellZoneSize = size;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(MsgType);
            switch (MsgType)
            {
                case 0: // Item message
                    writer.Write(Item);
                    break;

                case 1: // Size message
                    writer.Write(CellZoneSize);
                    break;

                default:
                    Log.Warning($"Unknown MsgType: {MsgType}");
                    break;
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            MsgType = reader.ReadInt32(); // Read the message type first
            switch (MsgType)
            {
                case 0: // Item message
                    Item = reader.ReadItemIndex();
                    break;

                case 1: // Size message
                    CellZoneSize = reader.ReadSingle();
                    break;

                default:
                    Log.Warning($"Unknown MsgType: {MsgType}");
                    break;
            }
        }

    }
}