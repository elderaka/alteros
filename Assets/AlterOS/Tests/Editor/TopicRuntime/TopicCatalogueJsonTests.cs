using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Reflection;
using NUnit.Framework;
using AlterOS.Topics;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicCatalogueJsonTests
    {
        [Test]
        public void StagedFieldNamesDeserializeAndValidateAsRuntimeTypes()
        {
            const string json = "{\"schemaVersion\":1,\"topicId\":\"greeting\",\"menuLabel\":\"\",\"prompt\":\"\",\"anchors\":[\"Hi\"],\"defaultReplayPolicy\":\"repeatable\",\"cooldownSeconds\":0,\"scenarios\":[{\"scenarioId\":\"morning\",\"priority\":2,\"baseWeight\":1,\"condition\":{\"kind\":\"timeWindow\",\"children\":[],\"statId\":\"\",\"operation\":\"\",\"value\":0,\"key\":\"\",\"expected\":true,\"startHour\":5,\"endHour\":12,\"objectKind\":\"\"},\"modifiers\":[],\"consumptionNodeId\":\"\",\"nodes\":[{\"id\":\"start\",\"speaker\":\"alterra\",\"text\":\"おはよう\",\"displayText\":\"Good morning\",\"language\":\"japanese\",\"voice\":\"alterra\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"gentle\",\"animation\":\"\"},\"choices\":[]}]}]}";
            TopicDefinition definition;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                definition = (TopicDefinition)new DataContractJsonSerializer(typeof(TopicDefinition)).ReadObject(stream);

            Assert.AreEqual("greeting", definition.topicId);
            Assert.AreEqual("Good morning", definition.scenarios[0].nodes[0].displayText);
            Assert.AreEqual("gentle", definition.scenarios[0].nodes[0].presentation.expression);
            Assert.AreEqual(0, TopicDefinitionValidator.Validate(definition).Count);
        }

        [Test]
        public void HeadlessValidatorInvokesTheProductionTopicValidator()
        {
            var validator = typeof(TopicDefinition).Assembly.GetType("AlterOS.Tools.TopicCatalogueValidator");
            Assert.IsNotNull(validator, "Headless catalogue validator has not been implemented.");
            var validate = validator.GetMethod("ValidateFile", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(validate);
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"schemaVersion\":1,\"topicId\":\"check\",\"menuLabel\":\"\",\"prompt\":\"\",\"anchors\":[\"Hi\"],\"defaultReplayPolicy\":\"repeatable\",\"cooldownSeconds\":0,\"scenarios\":[{\"scenarioId\":\"a\",\"priority\":0,\"baseWeight\":1,\"condition\":null,\"modifiers\":[],\"consumptionNodeId\":\"\",\"nodes\":[{\"id\":\"start\",\"speaker\":\"alterra\",\"text\":\"Hi\",\"displayText\":\"\",\"language\":\"\",\"voice\":\"\",\"audioKey\":\"\",\"nextId\":\"\",\"presentation\":{\"expression\":\"\",\"animation\":\"\"},\"choices\":[]}]}]}");
                var errors = (System.Collections.Generic.List<string>)validate.Invoke(null, new object[] { path });
                Assert.AreEqual(0, errors.Count, string.Join(" ", errors));
                File.WriteAllText(path, "{broken");
                Assert.Greater(((System.Collections.Generic.List<string>)validate.Invoke(null, new object[] { path })).Count, 0);
            }
            finally { File.Delete(path); }
        }
    }
}
