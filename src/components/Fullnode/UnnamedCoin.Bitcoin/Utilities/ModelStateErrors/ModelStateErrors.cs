﻿using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using UnnamedCoin.Bitcoin.Utilities.JsonErrors;

namespace UnnamedCoin.Bitcoin.Utilities.ModelStateErrors
{
    public static class ModelStateErrors
    {
        /// <summary>
        ///     Builds an <see cref="IActionResult" /> containing errors contained in the <see cref="ControllerBase.ModelState" />.
        /// </summary>
        /// <returns>A result containing the errors.</returns>
        public static IActionResult BuildErrorResponse(ModelStateDictionary modelState)
        {
            var errors = modelState.Values.SelectMany(e => e.Errors).ToList();
            return ErrorHelpers.BuildErrorResponse(
                HttpStatusCode.BadRequest,
                string.Join(Environment.NewLine, errors.Select(m => m.ErrorMessage)),
                string.Join(Environment.NewLine, errors.Select(m => m.Exception?.Message)));
        }
    }
}