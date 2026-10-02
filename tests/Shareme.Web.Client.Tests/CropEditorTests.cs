using System.Drawing;
using Shareme.Contracts.Assets;
using Shareme.Web.Client.Assets;

namespace Shareme.Web.Client.Tests;

public sealed class CropEditorTests
{
    [Fact]
    public void A_free_selection_becomes_fractions_of_the_preview()
    {
        var area = CropEditor.Fit(new RectangleF(400, 100, 800, 400), 1600, 800, null).ShouldNotBeNull();

        area.ShouldBe(new CropArea(0.25, 0.125, 0.5, 0.5));
        area.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_fixed_ratio_narrows_the_selection_around_its_centre_and_clamps_to_the_picture()
    {
        var square = CropEditor.Fit(new RectangleF(400, 0, 800, 400), 1600, 800, 1).ShouldNotBeNull();
        square.ShouldBe(new CropArea(0.375, 0, 0.25, 0.5));

        var clamped = CropEditor.Fit(new RectangleF(-100, -100, 5000, 5000), 1600, 800, null).ShouldNotBeNull();
        clamped.ShouldBe(new CropArea(0, 0, 1, 1));

        // a drag that starts left of the picture keeps only the part on it
        CropEditor.Fit(new RectangleF(-400, 0, 800, 800), 1600, 800, null).ShouldBe(new CropArea(0, 0, 0.25, 1));

        CropEditor.Fit(new RectangleF(10, 10, 0.5f, 100), 1600, 800, null).ShouldBeNull();
    }
}
