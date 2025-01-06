using RoR2;
using UnityEngine.Networking;

namespace An_Rnd
{
    public class An_Network : MessageBase
    {
        public ItemIndex Item { get; private set; }

        public An_Network() { } //this was present in the reference i used so i am using this too. I have no idea how networking stuff works, but it would not work any other way i tried (been 3 days at this point) so yeah

        public An_Network(ItemIndex passItem)
        {
            Item = passItem;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Item);
        }

        public override void Deserialize(NetworkReader reader)
        {
            Item = reader.ReadItemIndex();
        }

    }
}