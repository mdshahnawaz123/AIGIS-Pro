using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Options for <see cref="OnnxProvider"/>, bound from <c>Ai:Providers:onnx</c>.
/// </summary>
public sealed class OnnxOptions
{
    /// <summary>Gets or sets the path to the <c>.onnx</c> model file. Relative paths resolve against the application base directory.</summary>
    [Required]
    public string ModelPath { get; set; } = "assets/models/onnx/layer-classifier.onnx";

    /// <summary>Gets or sets the path to the JSON label file: a flat array of label strings, ordered to match the model's output dimension.</summary>
    [Required]
    public string LabelsPath { get; set; } = "assets/models/onnx/labels.json";

    /// <summary>Gets or sets the model input tensor name. Leave empty to use the model's first declared input.</summary>
    public string? InputTensorName { get; set; }

    /// <summary>Gets or sets the model output tensor name. Leave empty to use the model's first declared output.</summary>
    public string? OutputTensorName { get; set; }

    /// <summary>Gets or sets the feature vector length. Must match the dimension the model was trained on.</summary>
    [Range(16, 65536)]
    public int FeatureDimension { get; set; } = 512;

    /// <summary>Gets or sets the intra-operation thread count. Keep low on a desktop so the UI stays responsive.</summary>
    [Range(1, 64)]
    public int IntraOpNumThreads { get; set; } = 2;

    /// <summary>Gets or sets a value indicating whether the model output is raw logits and needs a softmax applied.</summary>
    public bool ApplySoftmax { get; set; } = true;
}
