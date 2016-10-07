﻿using System.Threading.Tasks;
using AllReady.Models;
using MediatR;
using System.Linq;
using System;
using System.Collections.Generic;
using AllReady.Areas.Admin.Features.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Extensions.Internal;

namespace AllReady.Areas.Admin.Features.Itineraries
{
    public class AddRequestsCommandHandler : IAsyncRequestHandler<AddRequestsCommand, bool>
    {
        private readonly AllReadyContext _context;
        private readonly IMediator _mediator;

        public AddRequestsCommandHandler(AllReadyContext context, IMediator mediator)
        {
            _context = context;
            _mediator = mediator;
        }

        public async Task<bool> Handle( AddRequestsCommand message )
        {
            //Itinerary itinerary = await _context.GetItineraryByIdAsync(message.ItineraryId);
            var itinerary = await _context.Itineraries
                .Where(x => x.Id == message.ItineraryId)
                .SingleOrDefaultAsync();

            if (itinerary == null)
            {
                // todo: sgordon: enhance this with a error message so the controller can better respond to the issue
                return false;
            }

            //var requestsToUpdate = await _context.Requests
            //    .Where(r => message.RequestIdsToAdd.Contains(r.RequestId.ToString()))
            //    .ToList();
            var requestsToUpdate = await _context.Requests.AsAsyncEnumerable()
                .Where(r => message.RequestIdsToAdd.Contains(r.RequestId.ToString()))
                .ToList();
            
            var foundRequests = new HashSet<string>(requestsToUpdate.Select(s => s.RequestId.ToString()));

            var notFound = message.RequestIdsToAdd.Where(m => !foundRequests.Contains(m));

            if (notFound.Any())
            {
                // Something went wrong as some of the ids passed in where not matched in the database
                // todo: sgordon: we should enhance the returned object to include a message so that the controller can provide better feedback to the user
                return false;
            }

            if (requestsToUpdate.Count > 0)
            {
                //var orderIndex = await _context.ItineraryRequests
                //    .Where(i => i.ItineraryId == itinerary.Id)
                //    .OrderByDescending(i => i.OrderIndex)
                //    .Select(i => i.OrderIndex)
                //    .FirstOrDefault();

                var orderIndex = await _context.ItineraryRequests.AsAsyncEnumerable()
                    .Where(i => i.ItineraryId == itinerary.Id)
                    .OrderByDescending(i => i.OrderIndex)
                    .Select(i => i.OrderIndex)
                    .FirstOrDefault();

                var itineraryRequestsToAdd = new List<ItineraryRequest>();

                foreach (var request in requestsToUpdate)
                {
                    orderIndex++;

                    if (request.Status == RequestStatus.Unassigned)
                    {
                        request.Status = RequestStatus.Assigned;

                        itineraryRequestsToAdd.Add(new ItineraryRequest
                        {
                            ItineraryId = itinerary.Id,
                            Request = request,
                            OrderIndex = orderIndex,
                            DateAssigned = DateTime.UtcNow // Note, we're storing system event dates as UTC time.
                        });


                        // todo: sgordon: Add a history record here and include the assigned date in the ItineraryRequest
                    }
                }

                //await _context.AddItineraryRequests(itineraryRequestsToAdd);
                _context.ItineraryRequests.AddRange(itineraryRequestsToAdd);
                await _context.SaveChangesAsync();

                //On Successful addition of request
                Func<Request, Itinerary, string> getNotificationMessage = ( r, i ) => String.Format(ItinerariesMessages.RequestAddedInitialNotificationFormat, i.Date);

                await _mediator.SendAsync(new NotifyRequestorsCommand
                {
                    Requests = requestsToUpdate,
                    Itinerary = itinerary,
                    NotificationMessageBuilder = getNotificationMessage
                });
            }

            return true;
        }
    }
}