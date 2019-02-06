// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Mvc.ApplicationModels
{
    /// <summary>
    /// <see cref="IActionModelConvention"/> that modifies <see cref="ActionModel.ActionName"/>
    /// to remove the Async suffix. <see cref="ActionModel.ActionName"/> is used to construct the
    /// route to the action, as well as view lookup.
    /// </summary>
    public class SuppressAsyncSuffixInActionNameConvention : IActionModelConvention
    {
        /// <summary>
        /// Determines if the convention applies to specified <paramref name="action"/>.
        /// Defaults to returning <see langword="true"/>.
        /// </summary>
        /// <param name="action">The <see cref="ActionModel"/>.</param>
        /// <returns><see langword="true"/> if the convention should apply to <paramref name="action"/>.</returns>
        protected virtual bool ShouldApply(ActionModel action) => true;

        /// <inheritdoc />
        public void Apply(ActionModel action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!ShouldApply(action))
            {
                return;
            }

            const string Suffix = "Async";

            if (action.ActionName.EndsWith(Suffix, StringComparison.Ordinal))
            {
                action.ActionName = action.ActionName.Substring(0, action.ActionName.Length - Suffix.Length);
            }
        }
    }
}
