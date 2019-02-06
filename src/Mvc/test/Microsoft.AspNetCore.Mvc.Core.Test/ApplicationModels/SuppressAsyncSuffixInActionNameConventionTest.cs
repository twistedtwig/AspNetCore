// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.ApplicationModels
{
    public class SuppressAsyncSuffixInActionNameConventionTest
    {
        private readonly ActionModel ActionModel = new ActionModel(typeof(object).GetMethod(nameof(string.ToString)), Array.Empty<object>());

        [Fact]
        public void Apply_RemovesSuffix()
        {
            // Arrange
            ActionModel.ActionName = "ListItemsAsync";
            var convention = new SuppressAsyncSuffixInActionNameConvention();

            // Act
            convention.Apply(ActionModel);

            // Assert
            Assert.Equal("ListItems", ActionModel.ActionName);
        }

        [Fact]
        public void Apply_NoOpsIfNameDoesNotEndWithSuffix()
        {
            // Arrange
            ActionModel.ActionName = "ListItems";
            var convention = new SuppressAsyncSuffixInActionNameConvention();

            // Act
            convention.Apply(ActionModel);

            // Assert
            Assert.Equal("ListItems", ActionModel.ActionName);
        }

        [Fact]
        public void Apply_NoOpsIfShouldApplyReturnsFalse()
        {
            // Arrange
            ActionModel.ActionName = "ListItemsAsync";
            var convention = new TestSuppressAsyncSuffixInActionNameConvention();

            // Act
            convention.Apply(ActionModel);

            // Assert
            Assert.Equal("ListItemsAsync", ActionModel.ActionName);
        }

        private class TestSuppressAsyncSuffixInActionNameConvention : SuppressAsyncSuffixInActionNameConvention
        {
            protected override bool ShouldApply(ActionModel action) => false;
        }
    }
}
