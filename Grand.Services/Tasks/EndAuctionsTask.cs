﻿using Grand.Core.Domain.Localization;
using Grand.Services.Catalog;
using Grand.Services.Customers;
using Grand.Services.Logging;
using Grand.Services.Messages;
using Grand.Services.Orders;
using System.Linq;
using System.Threading.Tasks;

namespace Grand.Services.Tasks
{
    /// <summary>
    /// Represents a task end auctions
    /// </summary>
    public partial class EndAuctionsTask : IScheduleTask
    {
        private readonly IProductService _productService;
        private readonly IAuctionService _auctionService;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly LocalizationSettings _localizationSettings;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ICustomerService _customerService;
        private readonly ILogger _logger;

        public EndAuctionsTask(IProductService productService, IAuctionService auctionService, IQueuedEmailService queuedEmailService,
            IWorkflowMessageService workflowMessageService, LocalizationSettings localizationService, IShoppingCartService shoppingCartService,
            ICustomerService customerService, ILogger logger)
        {
            this._productService = productService;
            this._auctionService = auctionService;
            this._workflowMessageService = workflowMessageService;
            this._localizationSettings = localizationService;
            this._shoppingCartService = shoppingCartService;
            this._customerService = customerService;
            this._logger = logger;
        }

        /// <summary>
        /// Executes a task
        /// </summary>
        public async Task Execute()
        {
            var auctionsToEnd = await _auctionService.GetAuctionsToEnd();
            foreach (var auctionToEnd in auctionsToEnd)
            {
                var bid = (await _auctionService.GetBidsByProductId(auctionToEnd.Id)).OrderByDescending(x => x.Amount).FirstOrDefault();
                if (bid == null)
                {
                    await _auctionService.UpdateAuctionEnded(auctionToEnd, true);
                    await _workflowMessageService.SendAuctionEndedStoreOwnerNotification(auctionToEnd, _localizationSettings.DefaultAdminLanguageId, null);
                    continue;
                }

                var warnings = await _shoppingCartService.AddToCart(_customerService.GetCustomerById(bid.CustomerId).GetAwaiter().GetResult(), bid.ProductId, Core.Domain.Orders.ShoppingCartType.Auctions,
                    bid.StoreId, customerEnteredPrice: bid.Amount);

                if (!warnings.Any())
                {
                    bid.Win = true;
                    await _auctionService.UpdateBid(bid);
                    await _workflowMessageService.SendAuctionEndedStoreOwnerNotification(auctionToEnd, _localizationSettings.DefaultAdminLanguageId, bid);
                    await _workflowMessageService.SendAuctionEndedCustomerNotificationWin(auctionToEnd, null, bid);
                    await _workflowMessageService.SendAuctionEndedCustomerNotificationLost(auctionToEnd, null, bid);
                    await _auctionService.UpdateAuctionEnded(auctionToEnd, true);
                }
                else
                {
                    await _logger.InsertLog(Core.Domain.Logging.LogLevel.Error, $"EndAuctionTask - Product {auctionToEnd.Name}", string.Join(",", warnings.ToArray()));
                }
            }
        }
    }
}