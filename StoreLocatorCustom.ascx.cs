using System;
using System.Linq;
using System.Web.UI.WebControls;
using Telerik.Sitefinity.DynamicModules;
using Telerik.Sitefinity.Utilities.TypeConverters;
using System.Device.Location;
using Telerik.Sitefinity.DynamicModules.Model;
using Telerik.Sitefinity.GenericContent.Model;
using Telerik.Sitefinity.Services;
using Telerik.Sitefinity.GeoLocations.Model;
using Telerik.Sitefinity.GeoLocations;
using System.Net;
using System.IO;
using System.Xml;
using System.ComponentModel;
using System.Collections.Generic;

namespace SitefinityWebApp.Custom
{
    public partial class StoreLocatorCustom : System.Web.UI.UserControl
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            btnFindStores.Command += btnFindStores_Command;
            ddlDistance.SelectedIndexChanged += new EventHandler(ddlDistance_SelectedIndexChanged);

            if (!IsPostBack)
            {
                BindStores();
            }
        }

        void BindStores()
        {
            var manager = DynamicModuleManager.GetManager();
            Type storeType = TypeResolutionService.ResolveType("Telerik.Sitefinity.DynamicTypes.Model.Stores.Store");
            var stores = manager.GetDataItems(storeType)
                                .Where(s => s.Status == ContentLifecycleStatus.Live);
            var radius = double.Parse(ddlDistance.SelectedValue);
            var userLocation = GetCoordinate(txtSourceZip.Text.Trim());            
            var itemFilter = new ItemFilter { ContentType = storeType.ToString()};
            IEnumerable<IGeoLocation> geolocations;
            stores = (manager as IGeoLocationManager).FilterByGeoLocation(stores, userLocation.Latitude, userLocation.Longitude, radius, out geolocations, itemFilter: itemFilter);
            var sortedStores = (manager as IGeoLocationManager).SortByDistance(stores, geolocations, userLocation.Latitude, userLocation.Longitude, DistanceSorting.Asc);
            
            DynamicContent firstStore = sortedStores.FirstOrDefault();
            if (firstStore != null)
            {
                var address = firstStore.GetAddressFields().First().Value;
                litDefaultLat.Text = String.Format("{0}", address.Latitude);
                litDefaultLong.Text = String.Format("{0}", address.Longitude);
            }

            listStores.DataSource = sortedStores;
            listStores.DataBind();
            lblStoreCount.Text = sortedStores.Count().ToString();
        }

        void CalculateStoreDistances(IQueryable<DynamicContent> stores)
        {
            string sourceZip = txtSourceZip.Text.Trim();
            GeoCoordinate sourceCoords = String.IsNullOrWhiteSpace(sourceZip) ? new GeoCoordinate(0, 0) : GetCoordinate(sourceZip);
            foreach (var store in stores)
            {
                var properties = TypeDescriptor.GetProperties(store);
                PropertyDescriptor zipProperty = properties["Zip"];
                string storeZip = zipProperty.GetValue(store).ToString();
                GeoCoordinate storeCoords = null;
                double storeLatitude = Convert.ToDouble(properties["Latitude"].GetValue(store));
                double storeLongitude = Convert.ToDouble(properties["Longtitude"].GetValue(store));
                if (storeLatitude == 0 && storeLatitude == 0)
                {
                    storeCoords = GetCoordinate(storeZip);
                }
                else
                {
                    storeCoords = new GeoCoordinate(storeLatitude, storeLongitude);
                }

                double distanceMeters = String.IsNullOrWhiteSpace(sourceZip) ? 0 : sourceCoords.GetDistanceTo(storeCoords);
                double distanceMiles = distanceMeters * 0.000621371192;
                PropertyDescriptor distanceProperty = properties["Distance"];
                distanceProperty.SetValue(store, (decimal)Math.Round(distanceMiles, 2));
                PropertyDescriptor latProperty = properties["Latitude"];
                latProperty.SetValue(store, (decimal)Math.Round(storeCoords.Latitude, 5));
                PropertyDescriptor longProperty = properties["Longtitude"];
                longProperty.SetValue(store, (decimal)Math.Round(storeCoords.Longitude, 5));
            }
        }

        /// <summary>
        /// Called when the Find Stores button is clicked.  Simply rebinds the store list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void btnFindStores_Command(object sender, CommandEventArgs e)
        {
            BindStores();
        }

        /// <summary>
        /// Called when the used changes distance drop down.  Simply rebinds the store list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void ddlDistance_SelectedIndexChanged(object sender, EventArgs e)
        {
            BindStores();
        }

        /// <summary>
        /// GetCoordinate: Returns a GetCoordinate object containing the latitude and longitude of the
        /// location of the given zip code.
        /// This example uses a call to Google's Geocoding API (V3) to convert the zip code into latitude and long
        /// See: https://developers.google.com/maps/documentation/geocoding/
        /// </summary>
        /// <param name="zip"></param>
        /// <returns></returns>
        GeoCoordinate GetCoordinate(string zip)
        {
            if (String.IsNullOrWhiteSpace(zip))
            {
                return new GeoCoordinate(0, 0);
            }

            // Setup the url for posting to google.  We are requesting "XML" as the return data set for the given zip code
            string url = string.Format("http://maps.googleapis.com/maps/api/geocode/xml?address={0}&sensor=false", zip);

            string xmlResponse = PostRequest(url, new byte[1], 3000);

            // We now have an XML response string from Google so load it into an XmlDocument so we can parse the data.
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlResponse);

            // The status node tells us if Google is returned valid data "OK", or an error
            XmlNode nodeStatus = xmlDoc.SelectSingleNode("/GeocodeResponse/status");

            double sourceLat = 0;
            double sourceLong = 0;

            if (nodeStatus.InnerText == "OK")
            {
                // We have valid data from google so get the latitude and longtidue values from the returned XML.

                XmlNode nodeLat = xmlDoc.SelectSingleNode("/GeocodeResponse/result/geometry/location/lat");
                XmlNode nodeLong = xmlDoc.SelectSingleNode("/GeocodeResponse/result/geometry/location/lng");
                Double.TryParse(nodeLat.InnerText, out sourceLat);
                Double.TryParse(nodeLong.InnerText, out sourceLong);
            }
            else
            {
                // Note there seems to be a limit to the number of times this api is called.  Unsure how this limit
                // is determined:  If it is a limit to a quick # of calls or a # of calls in a given time period.
                // It is up to you to determine how you want to handle the situation where the data returned is not valid.
                // You can throw an error (not great) or return an zero lat, long GetCoordinate (better).
                throw new Exception("Error returned from Google : " + nodeStatus.InnerText);
                //return new GeoCoordinate(sourceLat, sourceLong);
            }

            return new GeoCoordinate(sourceLat, sourceLong);
        } // GetLatLong       

        public static string PostRequest(string url, byte[] data, int timeout, string contentType = "application/x-www-form-urlencoded", bool keepAlive = false)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = timeout;
                request.ContentType = contentType;
                request.KeepAlive = keepAlive;

                HttpWebResponse response = SendRequest(request, data);

                return ReadResponse(response.GetResponseStream());
            }
            catch (WebException ex)
            {
                return ReadResponse(ex.Response.GetResponseStream());
            }
        }

        private static HttpWebResponse SendRequest(HttpWebRequest request, byte[] data)
        {
            // Send the data out over the wire
            try
            {
                Stream requestStream = request.GetRequestStream();
                requestStream.Write(data, 0, data.Length);
                requestStream.Close();
            }
            catch (Exception ex)
            {
                throw new WebException("An error occured while connecting", ex);
            }

            HttpWebResponse response = null;

            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception wex)
            {
                throw new WebException("An error occured while connecting", wex);
            }
            return response;
        }

        private static string ReadResponse(Stream response)
        {
            string responseString = "";
            using (StreamReader sr = new StreamReader(response))
            {
                responseString = sr.ReadToEnd();
                sr.Close();
            }

            return responseString;
        }
    } // class StoreLocatorCustom

}