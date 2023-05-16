# LeapAI.Net

A Dotnet SDK for LeapAI. [LeapAI](https://www.tryleap.ai/) is a platform for generating images, editing them, fine-tuning machine learning models, retrieving text context and more with best-in-class APIs.

*Not Official*.

*LeapAI doesn't have an official .NET SDK*

## Installation
You can install the package from [NuGet](https://www.nuget.org/packages/LeapAI.Net/). Here are some other ways to install it:

### .NET CLI
```
dotnet add package LeapAI.Net
```

### Package Manager
```
NuGet\Install-Package LeapAI.Net
```

For release notes, please see the bottom of this page.

## Features
- [x] Image Creation, retrieval, and deletion
- [x] Machine Learning Model Fine-tuning and management
- [x] Image Remixing
- [x] Account Project Management (What the LeapAI API supports)
- [x] Sample Project

Please visit https://www.tryleap.ai/ to get your API key.

## Sample Usages
The repository contains a sample project named **LeapAI.Net.Test** that you can refer to for a better understanding of how the library works. However, please exercise caution while experimenting with it, as some of the test methods may result in unintended consequences such as file deletion or fine tuning.

*!! It is highly recommended that you use a separate account instead of your primary account while using the test project. This is because some test methods may add or delete your files and models, which could potentially cause unwanted issues. !!*

Your API Key comes from here --> https://www.tryleap.ai/projects/

### Code Samples:
#### Program.cs
```csharp
// Get API key from an environment variable (alternative options below)
var imageJobService = new ImageJobService(new LeapAiOptions()
{
    ApiKey =  Environment.GetEnvironmentVariable("LEAPAI_API_KEY"),
    DefaultModelId = PreTrainedModels.OpenJourney4
});

// You can also set the default model id at any point
imageJobService.DefaultModelId = PreTrainedModels.OpenJourney4;

// Create a new image job
var imageJob = await imageJobService.CreateImageJobAsync(new ImageJobCreateRequest("A beautiful sunset on a beach")
{
    NegativePrompt = "asymmetric, watermarks",
    Steps = 50,
    Width = 1024,
    Height = 1024,
    NumberOfImages = 4,
    PromptStrength = 15,
    Seed = 4523184,
    RestoreFaces = true,
    EnhancePrompt = true,
    UpscaleBy = "x1",
    Sampler = StaticValues.ImageStatics.Sampler_ddim
});
var jobId = imageJob.Id;
var attempts = 0;
var images = new List<ImageData>();
var chosenModel = imageJob.ModelId;

// Check until you've hit a certain number of retries
while (attempts < 20)
{
    await Task.Delay(5000);

    var imageJobCheck = await imageJobService.GetImageJobAsync(chosenModel, 
        jobId);

    if (imageJobCheck.State == "finished" && imageJobCheck.Images != null)
    {
        images.AddRange(imageJobCheck.Images);

        break;
    }

    attempts++;
}

if (images.Count == 0)
{
    // The images probably took a little longer to generate
	Console.WriteLine("Images are taking longer to generate than expected");
	
    return;
}

// Download the images using an http client
foreach (var image in images)
{
	using var client = new HttpClient();
	
    var response = await client.GetAsync(image.Uri);
	var bytes = await response.Content.ReadAsByteArrayAsync();

	File.WriteAllBytes($"{image.Id}.png", bytes);
}

// Delete all image jobs
// First, get all pre-trained models
var preTrainedModels = PreTrainedModels.All;

// loop through each
foreach (var model in preTrainedModels)
{
    var modelId = model.Value;

    // get all image jobs for the model
    var imageJobs = await imageJobService.GetImageJobsAsync(modelId);

    if (imageJobs == null || !imageJobs.Any())
        continue;

    // loop through each image job
    foreach (var imageJob in imageJobs)
    {
        // delete the image job
        await imageJobService.DeleteImageJobAsync(modelId, imageJob.Id);
    }
}

// ask the user if they want to remix an image
Console.WriteLine("Do you want to remix an image? (y/n)");
var remixImage = Console.ReadLine();

if (!string.IsNullOrEmpty(remixImage) &&
    remixImage.Equals("y", StringComparison.InvariantCultureIgnoreCase))
{
    //ask the user for the prompt
    Console.WriteLine("What is the prompt to use for the remix?");
    var remixPrompt = Console.ReadLine();

    // check if the remix prompt is null or empty
    if (string.IsNullOrEmpty(remixPrompt))
    {
        Console.WriteLine("The remix prompt cannot be null or empty");
        Console.ReadLine();

        return;
    }

    var remixService = new RemixJobService(new LeapAiOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("LEAPAI_API_KEY"),
        ApiVersion = "v1",
        DefaultModelId = chosenModel
    });

    // ask the user if they wish to provide a local image or an image url
    Console.WriteLine("Do you want to provide a local image or an image url? (L/U)");
    var imageType = Console.ReadLine();

    RemixJobCreateResponse remixJob = null;

    if (!string.IsNullOrEmpty(imageType) &&
        imageType.Equals("L", StringComparison.InvariantCultureIgnoreCase))
    {
        // ask the user for the local image path
        Console.WriteLine("What is the local image path?");
        var localImagePath = Console.ReadLine();

        if (!string.IsNullOrEmpty(localImagePath))
        {
            // check to make sure the file exists
            if (!File.Exists(localImagePath))
            {
                Console.WriteLine("The file does not exist");
                Console.ReadLine();
                return;
            }

            var request = new RemixJobCreateRequest
            {
                Mode = StaticValues.RemixModes.Canny,
                Prompt = prompt,
                NegativePrompt = "Disfigured, asymmetric",
                NumberOfImages = 4,
                Steps = 25
            };
            remixJob = await remixService.CreateRemixJobFromFileAsync(request,
                localImagePath);
        }
    }
    else if (!string.IsNullOrEmpty(imageType) &&
        imageType.Equals("U", StringComparison.InvariantCultureIgnoreCase))
    {
        // ask the user for the image url
        Console.WriteLine("What is the image url?");
        var imageUrl = Console.ReadLine();

        if (!string.IsNullOrEmpty(imageUrl))
        {
            var request = new RemixJobCreateRequest
            {
                Mode = StaticValues.RemixModes.Canny,
                Prompt = prompt,
                NegativePrompt = "Disfigured, asymmetric",
                NumberOfImages = 4,
                ImageUrl = imageUrl,
                Steps = 25
            };
            remixJob = await remixService.CreateRemixJobFromUrlAsync(request);
        }
    }

    // check if the remix job was created successfully
    if (remixJob == null)
    {
        Console.WriteLine("The remix job was not created successfully. Please try again");
        Console.ReadLine();

        return;
    }

    Console.WriteLine("Remix job created successfully");
    Console.WriteLine("Checking for remixed images...");

    // check periodically to see if the remix job is complete
    var attempts = 0;

    // Check until you've hit a certain number of retries
    while (attempts < 50)
    {
        await Task.Delay(5000);

        var remixJobCheck = await remixJobService.GetRemixJobAsync(remixJob.ModelId,
            remixJob.Id);

        if (remixJobCheck.Status == "finished" && remixJobCheck.Images != null)
        {
            var sample = 1;

            foreach (var image in remixJobCheck.Images)
            {
                // use HttpClient to download the image
                using var client = new HttpClient();

                var imageBytes = await client.GetByteArrayAsync(image.Uri);
                var imageFileName = $"{path}\\{remixJobCheck.Id}_{sample}.png";

                // save the image to a file
                await File.WriteAllBytesAsync(imageFileName, imageBytes);

                sample++;
            }
        }

        attempts++;
    }
}

// ask the user if they would like to delete a project
Console.WriteLine("Do you want to delete a project? (y/n)");
var deleteProject = Console.ReadLine();

if (!string.IsNullOrEmpty(deleteProject) &&
    deleteProject.Equals("y", StringComparison.InvariantCultureIgnoreCase)) 
{
    // ask the user for the project id
    Console.WriteLine("What is the project id?");
    var projectId = Console.ReadLine();

    if (!string.IsNullOrEmpty(projectId))
    {
        var projectService = new ProjectService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var result = await projectService.DeleteProjectAsync(projectId);

        if (string.IsNullOrEmpty(result))
            Console.WriteLine("Project deleted successfully!");
        else 
            Console.WriteLine(result);
    }
}

// ask the user if they would like to fine tune a model
Console.WriteLine("Do you want to fine tune a model? (y/n)");
var fineTuneModel = Console.ReadLine();

if (!string.IsNullOrEmpty(fineTuneModel) &&
    fineTuneModel.Equals("y", StringComparison.InvariantCultureIgnoreCase)) 
{
    // ask the user what title they want to use
    Console.WriteLine("What title do you want to use?");
    var title = Console.ReadLine();

    if (string.IsNullOrEmpty (title))
    {
        Console.WriteLine("You need to enter a title");
        Console.ReadLine();
                
        return;
    }

    // ask the user what subject keyword they want to use
    Console.WriteLine("What subject keyword do you want to use?");
    var subjectKeyword = Console.ReadLine();

    if (string.IsNullOrEmpty(subjectKeyword))
    {
        Console.WriteLine("You need to enter a subject keyword");
        Console.ReadLine();

        return;
    }

    var modelType = StaticValues.ModelTypes.Style;

    // create the model
    var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
    {
        ApiKey = leapaiApiKey,
        ApiVersion = "v1"
    });
    var createModelRequest = new ModelCreateRequest
    {
        Title = title,
        SubjectKeyword = subjectKeyword
    };

    if (!string.IsNullOrEmpty(subjectIdentifier))
        request.SubjectIdentifier = subjectIdentifier;
    if (!string.IsNullOrEmpty(subjectType))
        request.SubjectType = subjectType;

    var model = await modelFineTuningService.CreateModelAsync(createModelRequest);

    // check to ensure the model was created (You don't have to do this exactly.
    // this is just some examples of how you can check to see if the model was created)
    var modelListResponse = await modelFineTuningService.ListModelsAsync();
    var modelListObjectResponse = await modelFineTuningService.ListModelsGetObjectAsync();
    var modelExistsInList = modelListResponse.Any(m => m.Id == model.Id) 
        && modelListObjectResponse.Data != null
        && modelListObjectResponse.Data.Any(m => m.Id == model.Id);
    var exactModelResponse = await modelFineTuningService.RetrieveModelAsync(model.Id);
    var exactModelExists = exactModelResponse != null;

    if (string.IsNullOrEmpty(model.Id) && !modelExistsInList && !exactModelExists)
    {
        Console.WriteLine("Model was not created. Check your Leap AI account.");
        Console.ReadLine();

        return;
    }

    var newModelId = model.Id;
    var imagesUploaded = 0;
    var promptForImages = true;

    while (promptForImages)
    {
        // ask the user if they want to upload an image or link to an image
        Console.WriteLine("Do you want to provide a local image or an image url? (L/U)");
        var imageType = Console.ReadLine();

        if (!string.IsNullOrEmpty(imageType) &&
            imageType.Equals("L", StringComparison.InvariantCultureIgnoreCase))
        {
            // ask the user for the path to the image
            Console.WriteLine("What is the path to the image(s)? (Can be a directory)");
            var imagePath = Console.ReadLine();

            if (string.IsNullOrEmpty(imagePath))
            {
                Console.WriteLine("You need to enter a path to the image");
                Console.ReadLine();
                        
                continue;
            }
                    
            // if the path is a directory, loop through the files and upload them
            if (Directory.Exists(imagePath))
            {
                var files = Directory.GetFiles(imagePath);

                foreach (var file in files)
                {
                    // upload the image
                    await modelFineTuningService.UploadImageSampleFromFileAsync(newModelId, file);
                            
                    imagesUploaded++;
                }
            }
            else if (!File.Exists(imagePath))
            {
                Console.WriteLine("The path you entered is not valid");
                Console.ReadLine();
                        
                continue;
            }
            else
            {
                // upload the image
                await modelFineTuningService.UploadImageSampleFromFileAsync(newModelId, imagePath);

                imagesUploaded++;
            }                    
        }
        else if (!string.IsNullOrEmpty(imageType) &&
            imageType.Equals("U", StringComparison.InvariantCultureIgnoreCase))
        {
            // ask the user for the url to the image
            Console.WriteLine("What is the url to the image?");
            var imageUrl = Console.ReadLine();

            if (string.IsNullOrEmpty(imageUrl))
            {
                Console.WriteLine("You need to enter a url to the image");
                Console.ReadLine();
                        
                continue;
            }

            // add the image to the model
            var linkImageSampleRequest = new ImageSampleCreateRequest
            {
                Images = new[] { imageUrl }
            };
            
            await modelFineTuningService.UploadImageSampleFromUrlAsync(newModelId, request);
                    
            imagesUploaded++;                    
        }

        // ask the user if they want to upload another image
        Console.WriteLine("Do you want to upload another image sample? (y/n)");
        var uploadAnotherImage = Console.ReadLine();

        if (!string.IsNullOrEmpty(uploadAnotherImage) &&
            uploadAnotherImage.Equals("y", StringComparison.InvariantCultureIgnoreCase))
            continue;                
        else
        {
            if (imagesUploaded > 0)
                promptForImages = false;
            else
                Console.WriteLine("You need to upload at least one image");
        }
    }

    // Check image samples to ensure samples were uploaded
    // (You don't have to do this, this is just some sample usage)
    var imageSamplesResponse = await modelFineTuningService.ListImageSamplesAsync(newModelId);
    var numberOfExistingSamples = imageSamplesResponse.Length;
    var singleSampleExists = false;

    if (numberOfExistingSamples > 0) 
    {
        var firstImage = await modelFineTuningService.GetImageSampleAsync(newModelId,
            imageSamplesResponse.First().Id);

        singleSampleExists = firstImage != null;
    }

    var imageSamplesExist = numberOfExistingSamples - numberOfImageSamples >= 0 
        && singleSampleExists;

    if (!imageSamplesExist)
    {
        Console.WriteLine("There appears to have been an issue uploading samples, " +
            "Check your Leap AI account");
        Console.ReadLine();
                
        return;
    }

    // ask the user which pre-trained model they'd like to use as a base
    Console.WriteLine("Time to train your model. Choose a pre-trained model to use " +
        "as a base.");
    var baseWeights = PreTrainedModels.OpenJourney4;

    Console.WriteLine("Queueing training job...");

    // Queue the training job
    var queueTrainingRequest = new ModelQueueTrainingRequest
    {
        BaseWeightsId = baseWeights
    };
    var modelTrainingJob = await modelFineTuningService.QueueModelTrainingJobAsync(newModelId,
        queueTrainingRequest);
    var modelVersionId = modelTrainingJob?.Id;
    var trainingJobCompleted = modelTrainingJob?.Status == "finished";

    Console.WriteLine("Training queued. Waiting for completion...");

    while (!trainingJobCompleted)
    {
        // List all model versions and retrieve an individual one
        var modelVersionStatusResponse = await modelFineTuningService.ListModelVersionsAsync(newModelId);
        var versionExists = modelVersionStatusResponse.Any(v => v.Id == modelVersionId);
        var modelVersionStatus = versionExists
            ? await modelFineTuningService.GetModelVersionAsync(newModelId, modelVersionId).Status;
            : modelVersionStatus = "No model with the given version found";

        if (modelVersionStatus == "No model with the given version found" ||
            modelVersionStatus == "failed")
        {
            Console.WriteLine("There appears to have been an issue. " +
                                "Check your Leap AI account");
            Console.ReadLine();

            return;
        }
        else if (modelVersionStatus == "finished")
        {
            trainingJobCompleted = true;
        }
        else
        {
            Console.WriteLine("Training job still running. Waiting 30 seconds...");
            Thread.Sleep(30000);
        }
    }

    Console.WriteLine("Training job completed. Archiving image samples...");

    // Archive image samples (not required, just an example)
    var imageSamples = await modelFineTuningService.ListImageSamplesAsync(newModelId);

    foreach (var sample in imageSamples)
    {
        await modelFineTuningService.ArchiveImageSampleAsync(newModelId, sample.Id);
    }

    Console.WriteLine("Image samples archived.");

    // You can now generate images with your new model by using the ImageJobService.
    // Simply pass in your model's ID as the DefaultModelId or use it as the chosen model
    // Check the LeapAI documentation for more information

    // Delete the model after using it to generate an image
    var deleteModelResult = await modelFineTuningService.DeleteModelAsync(newModelId);

    if (string.IsNullOrEmpty(deleteModelResult))
        Console.WriteLine("Model deleted successfully!");
    else
        Console.WriteLine(deleteModelResult);
}
```

### Alternative API Key retrieval options:
#### from a file
```csharp
// Get API key from a file where your key is the only text in the file
// This example uses a file named "LEAPAI_API_KEY.txt" in the same directory as the executable
var imageJobService = new ImageJobService(new LeapAiOptions()
{
	ApiKey =  File.ReadAllText("LEAPAI_API_KEY.txt"),
	DefaultModelId = PreTrainedModels.OpenJourney4
});
```

#### secrets.json
```json
 "LeapAIServiceOptions": {
    //"ApiKey":"Your api key goes here"
  },
```
 
Right click your project name in "solution explorer" then click "Manage User Secrets" for a good way to securely manage your api keys
*(See [here](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows) for more information on using user secrets.)*

## Notes:
Please note that due to time constraints, I was unable to thoroughly test all of the methods or fully document the library. If you encounter any issues, please do not hesitate to report them or submit a pull request - your contributions are always appreciated.

I initially developed this SDK for my personal use and later decided to share it with the community. As I have not maintained any open-source projects before, any assistance or feedback would be greatly appreciated. If you would like to contribute in any way, please feel free to reach out to me with your suggestions.

I will always be using the latest libraries, and future releases will frequently include breaking changes. Please take this into consideration before deciding to use the library. I want to make it clear that I cannot accept any responsibility for any damage caused by using the library. If you feel that this is not suitable for your purposes, you are free to explore alternative libraries.

## Notes concerning automated publishing to NuGet
Automatic publishing of the SDK project has been added. A PR to the nugetPackAndPublish branch must be approved and merged. Upon completion, the appropriate action workflow should run and package and publish to nuget. This will only perform a Patch update. If a minor/major version update is required, that will need to be done manually and then the patch repo variable will need to be reset to 0.

## Release Notes
### 1.0.0
- Added Model Fine-Tuning Service (NOTE: A credit card is required to train the model)
- Breaking changes to some methods in the ImageJobService class
- Update some XML Docs
- Update readme

### 0.0.6
- Added Remix Service (as of right now it only supports creating and retrieving projects)
- Update readme

### 0.0.5
- Added Project Service (as of right now it only supports deleting projects)
- Correct some XML Docs
- Update readme

### 0.0.4
- Fixed critical issues that prevented the library from working
- Added some convenience options
- Added more sample code
- Update readme