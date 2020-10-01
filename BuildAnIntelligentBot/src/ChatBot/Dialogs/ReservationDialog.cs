using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatBot.Models;
using ChatBot.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;

namespace ChatBot.Dialogs
{
  public class ReservationDialog : ComponentDialog
    {
    // Conversation steps
    public const string TimePrompt = "timePrompt";
    public const string AmountPeoplePrompt = "amountPeoplePrompt";
    public const string NamePrompt = "namePrompt";
    public const string ConfirmationPrompt = "confirmationPrompt";

    // Dialog IDs
    private const string ProfileDialog = "profileDialog";

    private readonly TextToSpeechService _ttsService;

    public ReservationDialog(
        IStatePropertyAccessor<ReservationData> userProfileStateAccessor,
        TextToSpeechService ttsService)
        : base(nameof(ReservationDialog))
        {
      UserProfileAccessor = userProfileStateAccessor ?? throw new ArgumentNullException(nameof(userProfileStateAccessor));

      _ttsService = ttsService;
      var waterfallSteps = new WaterfallStep[]
        {
        InitializeStateStepAsync,
        TimeStepAsync,
        AmountPeopleStepAsync,
        NameStepAsync,
        ConfirmationStepAsync,
        FinalStepAsync,
        };

        // Add control flow dialogs
        AddDialog(new WaterfallDialog(ProfileDialog, waterfallSteps));
        AddDialog(new TextPrompt(TimePrompt));
        AddDialog(new TextPrompt(AmountPeoplePrompt, AmountPeopleValidatorAsync));
        AddDialog(new TextPrompt(NamePrompt));
        AddDialog(new ConfirmPrompt(ConfirmationPrompt));

        }
    private async Task<DialogTurnResult> InitializeStateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context, () => null);
            if (state == null)
            {
                var reservationDataOpt = stepContext.Options as ReservationData;
                if (reservationDataOpt != null)
                {
                    await UserProfileAccessor.SetAsync(stepContext.Context, reservationDataOpt);
                }
                else
                {
                    await UserProfileAccessor.SetAsync(stepContext.Context, new ReservationData());
                }
            }

            return await stepContext.NextAsync();
        }

        private async Task<DialogTurnResult> TimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context);

            if (string.IsNullOrEmpty(state.Time))
            {
                var msg = "When do you need the reservation?";
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = msg,
                        // Add the message to speak
                    },
                };
                return await stepContext.PromptAsync(TimePrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

    private async Task<DialogTurnResult> AmountPeopleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context);

            if (stepContext.Result != null)
            {
                var time = stepContext.Result as string;
                state.Time = time;
            }

            if (state.AmountPeople == null)
            {
                var msg = "How many people will you need the reservation for?";
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = msg,
                        // Add the message to speak
                    },
                };
                return await stepContext.PromptAsync(AmountPeoplePrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

    private async Task<DialogTurnResult> NameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context);

            if (stepContext.Result != null)
            {
                state.AmountPeople = stepContext.Result as string;
            }

            if (state.FullName == null)
            {
                var msg = "And the name on the reservation?";
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = msg,
                        // Add the message to speak
                    },
                };
                return await stepContext.PromptAsync(NamePrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

    private async Task<DialogTurnResult> ConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context);

            if (stepContext.Result != null)
            {
                state.FullName = stepContext.Result as string;
            }

            if (state.Confirmed == null)
            {
                var msg = $"Ok. Let me confirm the information: This is a reservation for {state.Time} for {state.AmountPeople} people. Is that correct?";
                var retryMsg = "Please confirm, say 'yes' or 'no' or something like that.";

                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = msg,
                        // Add the message to speak
                    },
                    RetryPrompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = retryMsg,
                        // Add the retry message to speak
                    },
                };
                return await stepContext.PromptAsync(ConfirmationPrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

    private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await UserProfileAccessor.GetAsync(stepContext.Context);

            if (stepContext.Result != null)
            {
                var confirmation = (bool)stepContext.Result;
                string msg = null;
                if (confirmation)
                {
                    msg = $"Great, we will be expecting you this {state.Time}. Thanks for your reservation {state.FirstName}!";
                }
                else
                {
                    msg = "Thanks for using the Contoso Assistance. See you soon!";
                }

                await stepContext.Context.SendActivityAsync(msg);
            }

            return await stepContext.EndDialogAsync();
        }

    private async Task<bool> AmountPeopleValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            var value = promptContext.Recognized.Value?.Trim() ?? string.Empty;

            if (!int.TryParse(value, out int numberPeople))
            {
                var msg = "The amount of people should be a number.";
                await promptContext.Context.SendActivityAsync(msg)
                  .ConfigureAwait(false);
                return false;
            }
            else
            {
                promptContext.Recognized.Value = value;
                return true;
            }
        }
        public IStatePropertyAccessor<ReservationData> UserProfileAccessor { get; }
  }
}