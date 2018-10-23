﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

using Param_ItemNamespace.Helpers;
using Param_ItemNamespace.Core.Models;
using Param_ItemNamespace.Core.Services;
using Param_ItemNamespace.Services;
using Param_ItemNamespace.Views;

namespace Param_ItemNamespace.ViewModels
{
    public class ImageGalleryViewViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public const string ImageGalleryViewSelectedIdKey = "ImageGalleryViewSelectedIdKey";
        public const string ImageGalleryViewAnimationOpen = "ImageGalleryView_AnimationOpen";
        public const string ImageGalleryViewAnimationClose = "ImageGalleryView_AnimationClose";

        private readonly INavigationService _navigationService;
        private GridView _imagesGridView;

        public BindableCollection<SampleImage> Source { get; } = new BindableCollection<SampleImage>();

        public ImageGalleryViewViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            var view = GetView() as IImageGalleryViewPage;
            _imagesGridView = view.GetGridView();

            // TODO WTS: Replace this with your actual data
            Source.AddRange(SampleDataService.GetGallerySampleData());
        }

        public async Task LoadAnimationAsync()
        {
            var selectedImageId = ImagesNavigationHelper.GetImageId(ImageGalleryViewSelectedIdKey);
            if (!string.IsNullOrEmpty(selectedImageId))
            {
                var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation(ImageGalleryViewAnimationClose);
                if (animation != null)
                {
                    var item = _imagesGridView.Items.FirstOrDefault(i => ((SampleImage)i).ID == selectedImageId);
                    _imagesGridView.ScrollIntoView(item);
                    await _imagesGridView.TryStartConnectedAnimationAsync(animation, item, "galleryImage");
                }

                ImagesNavigationHelper.RemoveImageId(ImageGalleryViewSelectedIdKey);
            }
        }

        public void OnImageSelected(SampleImage image)
        {
            _imagesGridView.PrepareConnectedAnimation(ImageGalleryViewAnimationOpen, image, "galleryImage");
            ImagesNavigationHelper.AddImageId(ImageGalleryViewSelectedIdKey, image.ID);
            _navigationService.For<ImageGalleryViewDetailViewModel>()
                .WithParam(v => v.ID, image.ID)
                .Navigate();
        }
    }
}