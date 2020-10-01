// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatBot.Dialogs;
using ChatBot.Models;
using ChatBot.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatBot
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class EchoBot : IBot
    {
        private readonly EchoBotAccessors _accessors;

        // Add text to speech service

        // Add Dialogs set
        private readonly DialogSet _dialogs;

        // Add Luis Recognizer
        protected LuisRecognizer _luis;

        public EchoBot(EchoBotAccessors accessors, IOptions<MySettings> config, LuisRecognizer luisRecognizer, QnAMaker qna)
        {
            _accessors = accessors ?? throw new System.ArgumentNullException(nameof(accessors));
            QnA = qna ?? throw new ArgumentNullException(nameof(qna));

            // Initialize the dialogs
            _dialogs = new DialogSet(_accessors.ConversationDialogState);

            // Initialize the TTSS

            // Initialize the LUIS recognizer
            _luis = luisRecognizer;

            // Register the dialog
            _dialogs.Add(new ReservationDialog(_accessors.ReservationState, null));
        }

        private async Task TodaysSpecialtiesHandlerAsync(ITurnContext context)
        {
            var actions = new[]
            {
                new CardAction(type: ActionTypes.ShowImage, title: "Carbonara", value: "Carbonara", image: $"{BotConstants.Site}/carbonara.jpg"),
                new CardAction(type: ActionTypes.ShowImage, title: "Pizza", value: "Pizza", image: $"{BotConstants.Site}/pizza.jpg"),
                new CardAction(type: ActionTypes.ShowImage, title: "Lasagna", value: "Lasagna", image: $"{BotConstants.Site}/lasagna.jpg"),
            };

            var cards = actions
              .Select(x => new HeroCard
              {
                  Images = new List<CardImage> { new CardImage(x.Image) },
                  Buttons = new List<CardAction> { x },
              }.ToAttachment())
              .ToList();
            var activity = (Activity)MessageFactory.Carousel(cards, "For today we have:");

            await context.SendActivityAsync(activity);
        }

        // Add QnAMaker
        private QnAMaker QnA { get; } = null;

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);
                var dialogResult = await dialogContext.ContinueDialogAsync(cancellationToken);
                if (!turnContext.Responded)
                {
                    switch (dialogResult.Status)
                    {
                        case DialogTurnStatus.Empty:
                            // Check LUIS model
                            var luisResults = await _luis.RecognizeAsync(turnContext, cancellationToken).ConfigureAwait(false);
                            var topScoringIntent = luisResults?.GetTopScoringIntent();
                            var topIntent = topScoringIntent.Value.score > 0.5 ? topScoringIntent.Value.intent : string.Empty;
                            switch (topIntent) // need one case for each Intent in Luis
                            {
                                case "TodaysSpeciality":    //correct tutorial spelling
                                    await TodaysSpecialtiesHandlerAsync(turnContext);
                                    break;
                                case "ReserveTable":
                                    var amountPeople = luisResults.Entities["AmountPeople"] != null ? (string)luisResults.Entities["AmountPeople"]?.First : null;
                                    var time = GetTimeValueFromResult(luisResults);
                                    await ReservationHandlerAsync(dialogContext, amountPeople, time, cancellationToken);
                                    break;
                                default:
                                    var answers = await this.QnA.GetAnswersAsync(turnContext);

                                    if (answers is null || answers.Count() == 0)
                                    {
                                        await turnContext.SendActivityAsync("Sorry, I didn't understand that.");
                                    }
                                    else if (answers.Any())
                                    {
                                        // If the service produced one or more answers, send the first one.
                                        await turnContext.SendActivityAsync(answers[0].Answer);
                                    }
                                    break;
                            }

                            break;
                        case DialogTurnStatus.Waiting:
                            // The active dialog is waiting for a response from the user, so do nothing.
                            break;
                        case DialogTurnStatus.Complete:
                            await dialogContext.EndDialogAsync();
                            break;
                        default:
                            await dialogContext.CancelAllDialogsAsync();
                            break;
                    }
                }

                // Get the conversation state from the turn context.
                var state = await _accessors.ReservationState.GetAsync(turnContext, () => new ReservationData());

                // Set the property using the accessor.
                await _accessors.ReservationState.SetAsync(turnContext, state);

                // Save the new state into the conversation state.
                await _accessors.ConversationState.SaveChangesAsync(turnContext);
                await _accessors.UserState.SaveChangesAsync(turnContext);

            }
            else if (turnContext.Activity.Type == ActivityTypes.ConversationUpdate && turnContext.Activity.MembersAdded.FirstOrDefault()?.Id == turnContext.Activity.Recipient.Id)
            {
                var msg = "Hi! I'm a restaurant assistant bot. I can help you with your reservation.";
                await turnContext.SendActivityAsync(msg);
            }
        }

        private string GetTimeValueFromResult(RecognizerResult result)
        {
            var timex = (string)result.Entities["datetime"]?.First["timex"].First;
            if (timex != null)
            {
                timex = timex.Contains(":") ? timex : $"{timex}:00";
                return DateTime.Parse(timex).ToString("MMMM dd \\a\\t HH:mm tt");  //errors if no date in timex
            }

            return null;
        }

        private async Task ReservationHandlerAsync(DialogContext dialogContext, string amountPeople, string time, CancellationToken cancellationToken)
        {
            var state = await _accessors.ReservationState.GetAsync(dialogContext.Context, () => new ReservationData(), cancellationToken);
            state.AmountPeople = amountPeople;
            state.Time = time;
            await dialogContext.BeginDialogAsync(nameof(ReservationDialog));
        }

    }
}
