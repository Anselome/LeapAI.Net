using LeapAI.Net.SDK.ObjectModels.RequestModels;
using LeapAI.Net.SDK.ObjectModels;
using LeapAI.Net.SDK.Services.Image;
using LeapAI.Net.SDK.Services;
using Microsoft.Extensions.Configuration;
using LeapAI.Net.SDK.ObjectModels.ResponseModels;

namespace LeapAI.Net.Test;

public class Program
{
    public static void Main(string[] args)
    {
        // make async call from main method
        DoWork().Wait();
    }

    /// <summary>
    /// This method will run tests
    /// </summary>
    /// <returns>Nothing</returns>
    /// <exception cref="Exception"></exception>
    public static async Task DoWork()
    {
        var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json",
            optional: false, reloadOnChange: true);
        var configuration = builder.Build();
        var outputPath = configuration.GetSection("Configuration:OutputDirectory")
            .Value;

        // tell the user the output directory is not configured if it is not
        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine("You need to configure an output directory");
            Console.ReadLine();

            return;
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

            var modelType = GetModelType();

            // create the model
            var model = await CreateModel(title, subjectKeyword, null, modelType);

            // check to ensure the model was created
            var modelExistsInList = await CheckModels(model);
            var exactModelExists = await CheckExactModel(model);

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
                            await UploadImageSample(model.Id, file);
                            
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
                        await UploadImageSample(model.Id, imagePath);

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
                    await LinkImageSample(model.Id, imageUrl);
                    
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
            var imageSamplesExist = await CheckImageSamples(model.Id, imagesUploaded);

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
            var baseWeights = ChoosePreTrainedModel();

            Console.WriteLine("Queueing training job...");

            // Queue the training job
            var modelTrainingJob = await QueueTrainingJob(model.Id, baseWeights);
            var modelVersionId = modelTrainingJob?.Id;
            var trainingJobCompleted = modelTrainingJob?.Status == "finished";

            Console.WriteLine("Training queued. Waiting for completion...");

            while (!trainingJobCompleted)
            {
                // List all model versions and retrieve an individual one
                var modelVersionStatus = await CheckModelVersionStatus(newModelId, modelVersionId);

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

            // Archive image samples
            await ArchiveImageSamples(newModelId);

            Console.WriteLine("Image samples archived.");

            // Delete the model after using it to generate an image
            // ask the user if they want to generate an image with the new model
            Console.WriteLine("Do you want to generate an image with your new model? (y/n)");
            var generateImageWithNewModel = Console.ReadLine();

            if (!string.IsNullOrEmpty(generateImageWithNewModel) &&
                generateImageWithNewModel.Equals("y", StringComparison.InvariantCultureIgnoreCase))
            {
                // ask the user what they want an image of
                Console.WriteLine("What do you want an image of?");
                var newModelPrompt = Console.ReadLine();

                while (string.IsNullOrEmpty(newModelPrompt))
                {
                    // tell the user they need to enter a prompt
                    Console.WriteLine("You need to enter a prompt");
                    newModelPrompt = Console.ReadLine();
                }

                // ask the user how many images they want
                Console.WriteLine("How many images do you want?");
                var numberOfImagesFromNewModel = int.Parse(Console.ReadLine() ?? "1");
                var newModelImageJob = await CreateImageJob(newModelPrompt, newModelId, numberOfImagesFromNewModel);
                var newModelImageJobResponse = await CheckImageJobForImages(newModelId, newModelImageJob.Id);

                if (newModelImageJobResponse != null)
                {
                    // download the image
                    await DownloadImagesFromImageJob(newModelImageJobResponse, outputPath);
                }
            }

            // Delete the new model
            Console.WriteLine("Do you want to delete your new model? (y/n)");
            var deleteNewModel = Console.ReadLine();

            if (!string.IsNullOrEmpty(deleteNewModel) &&
                deleteNewModel.Equals("y", StringComparison.InvariantCultureIgnoreCase))
            {
                await DeleteModel(newModelId);
            }
        }

        // ask the user if they want to generate an image
        Console.WriteLine("Do you want to generate an image? (y/n)");
        var generateImage = Console.ReadLine();

        if (!string.IsNullOrEmpty(generateImage) &&
            generateImage.Equals("y", StringComparison.InvariantCultureIgnoreCase))
        {
            // ask the user what they want an image of
            Console.WriteLine("What do you want an image of?");
            var prompt = Console.ReadLine();

            // tell the user they need to enter a prompt
            if (string.IsNullOrEmpty(prompt))
            {
                Console.WriteLine("You need to enter a prompt");
                Console.ReadLine();

                return;
            }

            // ask the user if they want to see what their prompt generates with each model
            Console.WriteLine("Do you want to see what your prompt generates with each " +
                "model? (y/n)");
            var showPromptWithEachModel = Console.ReadLine();

            if (!string.IsNullOrEmpty(showPromptWithEachModel) &&
                showPromptWithEachModel.Equals("y", StringComparison.InvariantCultureIgnoreCase))
            {
                var imageJobs = new List<ImageJobCreateResponse>();

                // loop through PreTrainedModels
                foreach (var model in PreTrainedModels.All)
                {
                    // use the leapai api to generate an image
                    var imageResult = await CreateImageJob(prompt, model.Value, 1);

                    if (imageResult != null)
                        imageJobs.Add(imageResult);
                }

                foreach (var job in imageJobs)
                {
                    // check if the image job was created successfully
                    var imageJobResponse = await CheckImageJobForImages(job.ModelId, job.Id);

                    if (imageJobResponse != null)
                    {
                        // download the image
                        await DownloadImagesFromImageJob(imageJobResponse, outputPath);
                    }
                }
            }
            else
            {
                var chosenModel = ChoosePreTrainedModel();

                // ask the user how many images they want
                Console.WriteLine("How many images do you want?");
                var numberOfimages = int.Parse(Console.ReadLine() ?? "1");

                try
                {
                    // use the leapai api to generate an image
                    var imageResult = await CreateImageJob(prompt, chosenModel, numberOfimages);

                    // check if the image job was created successfully
                    var imageJob = await CheckImageJobForImages(chosenModel, imageResult.Id);

                    if (imageJob != null)
                    {
                        // download the image
                        await DownloadImagesFromImageJob(imageJob, outputPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating image: {ex.Message}");
                }
            }

            // ask the user if they want to delete all image jobs
            Console.WriteLine("Do you want to delete all image jobs? (y/n)");
            var deleteImageJobs = Console.ReadLine();

            if (!string.IsNullOrEmpty(deleteImageJobs) &&
                deleteImageJobs.Equals("y", StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine("Deleting image jobs...");

                await DeleteImageJobs();
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

            var chosenModel = ChoosePreTrainedModel();

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

                    remixJob = await RemixImageFromLocalPath(remixPrompt, chosenModel, localImagePath);
                }
            }
            else if (!string.IsNullOrEmpty(imageType) &&
                                    imageType.Equals("U", StringComparison.InvariantCultureIgnoreCase))
            {
                // ask the user for the image url
                Console.WriteLine("What is the image url?");
                var imageUrl = Console.ReadLine();

                if (!string.IsNullOrEmpty(imageUrl))
                    remixJob = await RemixImageFromUrl(remixPrompt, chosenModel, imageUrl);
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
            var remixJobResponse = await CheckRemixJobForImages(remixJob);

            if (remixJobResponse != null)
            {
                Console.WriteLine("Downloading images...");

                // download the images
                await DownloadImagesFromRemixJob(remixJobResponse, outputPath);
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
                await DeleteProject(projectId);
        }

        Console.WriteLine("Done!");
        Console.WriteLine("Press any key to exit...");
        Console.ReadLine();
    }

    private static string GetModelType()
    {
        // Prompt the user with all the possible StaticValues.ModelTypes
        Console.WriteLine("Which model type do you want to use?");
        Console.WriteLine("1. Animal");
        Console.WriteLine("2. Boy");
        Console.WriteLine("3. Businessman");
        Console.WriteLine("4. Businesswoman");
        Console.WriteLine("5. Cat");
        Console.WriteLine("6. Dog");
        Console.WriteLine("7. Girl");
        Console.WriteLine("8. Man");
        Console.WriteLine("9. Person");
        Console.WriteLine("10. Style");
        Console.WriteLine("11. Woman");

        var modelType = Console.ReadLine();

        return modelType switch
        {
            "1" => StaticValues.ModelTypes.Animal,
            "2" => StaticValues.ModelTypes.Boy,
            "3" => StaticValues.ModelTypes.Businessman,
            "4" => StaticValues.ModelTypes.Businesswoman,
            "5" => StaticValues.ModelTypes.Cat,
            "6" => StaticValues.ModelTypes.Dog,
            "7" => StaticValues.ModelTypes.Girl,
            "8" => StaticValues.ModelTypes.Man,
            "9" => StaticValues.ModelTypes.Person,
            "10" => StaticValues.ModelTypes.Style,
            "11" => StaticValues.ModelTypes.Woman,
            _ => StaticValues.ModelTypes.Person
        };
    }

    private static string ChoosePreTrainedModel()
    {
        // Ask the user which pre-trained model they want to use
        Console.WriteLine("Which model do you want to use?");
        Console.WriteLine("1. Stable Diffusion 1.5");
        Console.WriteLine("2. Stable Diffusion 2.1");
        Console.WriteLine("3. OpenJourney 4");
        Console.WriteLine("4. OpenJourney 2");
        Console.WriteLine("5. OpenJourney 1");
        Console.WriteLine("6. Modern Disney");
        Console.WriteLine("7. Future Diffusion");
        Console.WriteLine("8. Realistic Vision 2.0");

        var choice = Console.ReadLine();

        return choice switch
        {
            "1" => PreTrainedModels.StableDiffusion1_5,
            "2" => PreTrainedModels.StableDiffusion2_1,
            "3" => PreTrainedModels.OpenJourney4,
            "4" => PreTrainedModels.OpenJourney2,
            "5" => PreTrainedModels.OpenJourney1,
            "6" => PreTrainedModels.ModernDisney,
            "7" => PreTrainedModels.FutureDiffusion,
            "8" => PreTrainedModels.RealisticVision2_0,
            _ => PreTrainedModels.OpenJourney4
        };
    }

    // method to create an image job
    private static async Task<ImageJobCreateResponse?> CreateImageJob(string prompt,
        string chosenModel, int numberOfSamples = 1)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var imageJobService = new ImageJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1",
            DefaultModelId = chosenModel
        });

        ImageJobCreateResponse imageResult = null;

        try
        {
            imageResult = await imageJobService.CreateImageJobAsync(new ImageJobCreateRequest
            {
                Prompt = prompt,
                NegativePrompt = "asymmetric, bad hair, misshapen hands, too many fingers, " +
                    "malformed bodies, too many bodies, disfigured, more than one head",
                NumberOfImages = numberOfSamples,
                Height = 1024,
                Width = 1024,
                PromptStrength = 15,
                EnhancePrompt = true,
                RestoreFaces = true,
                Steps = 50,
                Seed = 4523184,
                UpscaleBy = "x1",
                Sampler = StaticValues.ImageStatics.Sampler_ddim
            });
        }
        catch (Exception ex)
        {
            // display the error message
            Console.WriteLine($"Error generating image for prompt {prompt}: {ex.Message}");
            Console.ReadLine();

            return null;
        }

        return imageResult;
    }

    // method to check periodically for the status of an image job to see if the images are ready
    private static async Task<ImageJobResponse?> CheckImageJobForImages(string chosenModel, string jobId)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var imageJobService = new ImageJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var attempts = 0;

        // Check until you've hit a certain number of retries
        while (attempts < 20)
        {
            await Task.Delay(5000);

            var imageJobCheck = await imageJobService.GetImageJobAsync(chosenModel, 
                jobId);

            if (imageJobCheck.State == "finished" && imageJobCheck.Images != null)
            {
                return imageJobCheck;
            }

            attempts++;
        }

        return null;
    }

    // method to download the images from an ImageJobRespose object
    private static async Task DownloadImagesFromImageJob(ImageJobResponse imageJobResponse, string path)
    {
        var sample = 1;
        var modelName = PreTrainedModels.All.Where(p => p.Value == imageJobResponse.ModelId)
            .FirstOrDefault().Key;

        foreach (var image in imageJobResponse.Images)
        {
            // use HttpClient to download the image
            using var client = new HttpClient();

            var imageBytes = await client.GetByteArrayAsync(image.Uri);
            var imageFileName = $"{path}\\{modelName}_{sample}.png";
            
            // save the image to a file
            await File.WriteAllBytesAsync(imageFileName, imageBytes);
            
            sample++;
        }
    }

    // method to retrieve all image jobs and delete them
    private static async Task DeleteImageJobs()
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var imageJobService = new ImageJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });

        // get all pre-trained models
        var preTrainedModels = PreTrainedModels.All;

        // loop through each pre-trained model
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
    }

    // method to use the leapai project service to delete a project
    private static async Task DeleteProject(string projectId)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
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

    private static async Task<RemixJobCreateResponse> RemixImageFromUrl(string? prompt, 
        string chosenModel, string imageUrl)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var remixService = new RemixJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1",
            DefaultModelId = chosenModel
        });
        var request = new RemixJobCreateRequest
        {
            Mode = StaticValues.RemixModes.Canny,
            Prompt = prompt,
            NegativePrompt = "Disfigured, asymmetric",
            NumberOfImages = 4,
            ImageUrl = imageUrl,
            Steps = 25
        };
        var remixJob = await remixService.CreateRemixJobFromUrlAsync(request);

        return remixJob;
    }

    private static async Task<RemixJobCreateResponse> RemixImageFromLocalPath(string prompt,
        string chosenModel, string localImagePath)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var remixService = new RemixJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1",
            DefaultModelId = chosenModel
        });
        var request = new RemixJobCreateRequest
        {
            Mode = StaticValues.RemixModes.Canny,
            Prompt = prompt,
            NegativePrompt = "Disfigured, asymetric",
            NumberOfImages = 4,
            Steps = 25
        };
        var remixJob = await remixService.CreateRemixJobFromFileAsync(request,
            localImagePath);

        return remixJob;
    }

    // method to check periodically for the status of a remix job to see if the images are ready
    private static async Task<RemixJobResponse?> CheckRemixJobForImages(RemixJobCreateResponse remixJob)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var remixJobService = new RemixJobService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var attempts = 0;

        // Check until you've hit a certain number of retries
        while (attempts < 50)
        {
            await Task.Delay(5000);

            var remixJobCheck = await remixJobService.GetRemixJobAsync(remixJob.ModelId,
                remixJob.Id);

            if (remixJobCheck.Status == "finished" && remixJobCheck.Images != null)
            {
                return remixJobCheck;
            }

            attempts++;
        }

        return null;
    }

    // method to download the images from a RemixJobRespose object
    private static async Task DownloadImagesFromRemixJob(RemixJobResponse remixJobResponse, string path)
    {
        var sample = 1;

        foreach (var image in remixJobResponse.Images)
        {
            // use HttpClient to download the image
            using var client = new HttpClient();

            var imageBytes = await client.GetByteArrayAsync(image.Uri);
            var imageFileName = $"{path}\\{remixJobResponse.Id}_{sample}.png";

            // save the image to a file
            await File.WriteAllBytesAsync(imageFileName, imageBytes);

            sample++;
        }
    }

    // method to create a model
    private static async Task<ModelCreateResponse> CreateModel(string title, string subjectKeyword,
        string? subjectIdentifier = "", string? subjectType = "Person")
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var request = new ModelCreateRequest
        {
            Title = title,
            SubjectKeyword = subjectKeyword
        };

        if (!string.IsNullOrEmpty(subjectIdentifier))
            request.SubjectIdentifier = subjectIdentifier;
        if (!string.IsNullOrEmpty(subjectType))
            request.SubjectType = subjectType;

        var model = await modelFineTuningService.CreateModelAsync(request);
        
        return model;
    }

    private static async Task<bool> CheckModels(ModelCreateResponse model)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.ListModelsAsync();
        var objectResponse = await modelFineTuningService.ListModelsGetObjectAsync();

        return response.Any(m => m.Id == model.Id) && objectResponse.Data != null
            && objectResponse.Data.Any(m => m.Id == model.Id);
    }

    private static async Task<bool> CheckExactModel(ModelCreateResponse model)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.RetrieveModelAsync(model.Id);

        return response != null;
    }

    private static async Task<ImageSampleCreateResponse[]> LinkImageSample(string modelId, string imageUrl)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var request = new ImageSampleCreateRequest
        {
            Images = new[] { imageUrl }
        };
        var response = await modelFineTuningService.UploadImageSampleFromUrlAsync(modelId, request);

        return response;
    }

    private static async Task<ImageSampleCreateResponse[]> UploadImageSample(string modelId, string imagePath)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.UploadImageSampleFromFileAsync(modelId, imagePath);

        return response;
    }

    private static async Task<bool> CheckImageSamples(string modelId, int numberOfImageSamples)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.ListImageSamplesAsync(modelId);
        var numberOfExistingSamples = response.Length;
        var singleSampleExists = false;

        if (numberOfExistingSamples > 0) 
        {
            var firstImage = await modelFineTuningService.GetImageSampleAsync(modelId,
                response.First().Id);

            singleSampleExists = firstImage != null;
        }

        return numberOfExistingSamples - numberOfImageSamples >= 0 && singleSampleExists;
    }

    private static async Task<ModelQueueTrainingResponse> QueueTrainingJob(string modelId, 
        string baseWeights)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var request = new ModelQueueTrainingRequest
        {
            BaseWeightsId = baseWeights
        };
        var response = await modelFineTuningService.QueueModelTrainingJobAsync(modelId,
            request);

        return response;
    }

    private static async Task<string> CheckModelVersionStatus(string modelId, string versionId)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.ListModelVersionsAsync(modelId);
        var versionExists = response.Any(v => v.Id == versionId);

        if (versionExists)
        {
            var versionDetails = await modelFineTuningService.GetModelVersionAsync(modelId, versionId);

            return versionDetails.Status;
        }
        else
        {
            return "No model with the given version found";
        }
    }

    private static async Task ArchiveImageSamples(string modelId)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var response = await modelFineTuningService.ListImageSamplesAsync(modelId);

        foreach (var sample in response)
        {
            await modelFineTuningService.ArchiveImageSampleAsync(modelId, sample.Id);
        }
    }

    private static async Task DeleteModel(string? newModelId)
    {
        var leapaiApiKey = File.ReadAllText("LEAPAI_API_KEY.txt");
        var modelFineTuningService = new ModelFineTuningService(new LeapAiOptions
        {
            ApiKey = leapaiApiKey,
            ApiVersion = "v1"
        });
        var result = await modelFineTuningService.DeleteModelAsync(newModelId);

        if (string.IsNullOrEmpty(result))
            Console.WriteLine("Model deleted successfully!");
        else
            Console.WriteLine(result);
    }
}