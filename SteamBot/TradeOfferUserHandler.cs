using SteamKit2;
using System;
using System.Collections.Generic;
using System.Linq;
using SteamAPI.TradeOffers;
using SteamAPI.TradeOffers.Objects;

namespace SteamBot
{
    public class TradeOfferUserHandler : UserHandler
    {
        public TradeOfferUserHandler(Bot bot, SteamID sid) : base(bot, sid) { }

        public override void OnTradeOfferReceived(TradeOffer tradeOffer)
        {
            if (IsAdmin)
            {
                try
                {
                    // see documentation for more info on when TradeOfferSteamException is thrown
                    TradeOffers.AcceptTrade(tradeOffer.Id);
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
        }

        public override void OnTradeOfferAccepted(TradeOffer tradeOffer)
        {
            var tradeOfferId = tradeOffer.Id;
            var myItems = tradeOffer.ItemsToGive;
            var userItems = tradeOffer.ItemsToReceive;

            Log.Info("Trade offer #{0} accepted. Items to give: {1}, Items to receive: {2}", tradeOfferId, myItems.Length, userItems.Length);

            // myItems is now in user inventory
            // userItems is now in bot inventory
        }

        public override void OnTradeOfferDeclined(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} has been declined.", tradeOffer.Id);
        }

        public override void OnTradeOfferCanceled(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} has been canceled by bot.", tradeOffer.Id);
        }

        public override void OnTradeOfferInvalid(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} is invalid, with state: {1}.", tradeOffer.Id, tradeOffer.State);
        }

        public override void OnTradeOfferFailedConfirmation(TradeOffer tradeOffer)
        {
            // confirmation failed, so cancel it just to be safe
            if (tradeOffer.IsOurOffer)
            {
                try
                {
                    TradeOffers.CancelTrade(tradeOffer);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
            else
            {
                try
                {
                    TradeOffers.DeclineTrade(tradeOffer);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
            Log.Warn("Trade offer #{0} failed to confirm. Cancelled the trade.");
        }

        public override void OnMessage(string message, EChatEntryType type)
        {
            if (IsAdmin)
            {
                if (message == "auth")
                {
                    Bot.SteamFriends.SendChatMessage(OtherSID, EChatEntryType.ChatMsg, Bot.SteamGuardAccount.GenerateSteamGuardCode());
                }
                else if (message == "test")
                {
                    var tradeOffer = TradeOffers.CreateTrade(OtherSID);
                    var inventories = FetchInventories(Bot.SteamClient.SteamID);
                    var csgoInventory = inventories.GetInventory(440, 2);
                    foreach (var item in csgoInventory)
                    {
                        tradeOffer.AddMyItem(440, 2, item.Id);
                        break;
                    }
                    try
                    {
                        var tradeOfferIdWithToken = tradeOffer.SendTradeWithToken("message", "");
                        if (tradeOfferIdWithToken > 0)
                        {
                            Log.Success("Trade offer sent: Offer ID " + tradeOfferIdWithToken);
                        }
                    }
                    catch (TradeOfferSteamException ex)
                    {
                        if (ex.ErrorCode == 11 || ex.ErrorCode == 16)
                        {
                            // trade offer might have been sent even though there was an error
                        }
                    }                    
                }
                else
                {
                    // EXAMPLE: creating a new trade offer
                    var tradeOffer = TradeOffers.CreateTrade(OtherSID);

                    //tradeOffer.AddMyItem(0, 0, 0);

                    var tradeOfferId = tradeOffer.SendTrade("message");
                    if (tradeOfferId > 0)
                    {
                        Log.Success("Trade offer sent : Offer ID " + tradeOfferId);
                    }
                    
                    try
                    {
                        // sending trade offer with token
                        // "token" should be replaced with the actual token from the other user
                        var tradeOfferIdWithToken = tradeOffer.SendTradeWithToken("message", "token");
                        if (tradeOfferIdWithToken > 0)
                        {
                            Log.Success("Trade offer sent: Offer ID " + tradeOfferIdWithToken);
                        }

                    }
                    catch (TradeOfferSteamException ex)
                    {
                        if (ex.ErrorCode == 11 || ex.ErrorCode == 16)
                        {
                            // trade offer might have been sent even though there was an error
                        }
                    }
                }                
            }
        }

        public override bool OnGroupAdd() { return false; }

        public override bool OnFriendAdd() { return IsAdmin; }

        public override void OnFriendRemove() { }

        public override void OnLoginCompleted() { }
    }
}
