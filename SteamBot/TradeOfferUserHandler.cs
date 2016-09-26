namespace SteamBot
{
public abstract class UserHandler
    namespace SteamBot
{
    public class TradeOfferUserHandler : UserHandler
    {
        public TradeOfferUserHandler(Bot bot, SteamID sid) : base(bot, sid) { }

        public override void OnTradeOfferChecked(TradeOffer tradeOffer)
        {
            // polling has been completed once for our sent trade offer, and it is still active
            // this will always be a trade offer from the bot
        }

        public override void OnTradeOfferReceived(TradeOffer tradeOffer)
        {
            if (IsAdmin)
            {
                try
                {
                    // see documentation for more info on when TradeOfferSteamException is thrown
                    ulong tradeId;
                    if (TradeOffers.AcceptTrade(tradeOffer.Id, out tradeId))
                    {
                        // you can do something with tradeId if you need to
                    }
                }
                catch (TradeOfferSteamException ex)
                {
                    if (ex.ErrorCode == 11 | ex.ErrorCode == 16)
                    {
                        // trade offer might have been accepted still
                    }
                }     
            }
            else
            {
                try
                {
                    TradeOffers.DeclineTrade(tradeOffer.Id);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
		public override bool OnGroupAdd() { return false; }
		public override bool OnFriendAdd() { return IsAdmin; }
		}
	}
}
}
